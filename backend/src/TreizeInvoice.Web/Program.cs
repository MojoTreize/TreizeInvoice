using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using System.Globalization;
using TreizeInvoice.Data;
using TreizeInvoice.Services.Auditing;
using TreizeInvoice.Services.Auth;
using TreizeInvoice.Services.Backup;
using TreizeInvoice.Services.Clients;
using TreizeInvoice.Services.Dashboard;
using TreizeInvoice.Services.Invoicing;
using TreizeInvoice.Services.Journal;
using TreizeInvoice.Services.Pdf;
using TreizeInvoice.Services.Security;
using TreizeInvoice.Services.Settings;
using TreizeInvoice.Services.Setup;
using TreizeInvoice.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Toute l'application est en allemand : montants 1.234,56 et dates 04.09.2026,
// quelle que soit la langue du serveur qui l'héberge.
var german = new CultureInfo("de-DE");
CultureInfo.DefaultThreadCurrentCulture = german;
CultureInfo.DefaultThreadCurrentUICulture = german;

// --- Emplacements de stockage ---
// Configurables (Storage:DataPath, Storage:LogoPath) : après publication, les
// chemins relatifs du dépôt de développement ne résolvent plus.
var dataDir = builder.Configuration["Storage:DataPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "..", "..", "data");
dataDir = Path.GetFullPath(dataDir);
Directory.CreateDirectory(dataDir);

var archiveDir = Path.Combine(dataDir, "archive");
var dbPath = Path.Combine(dataDir, "treizeinvoice.db");

var logoPath = Path.GetFullPath(builder.Configuration["Storage:LogoPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath,
        "..", "..", "..", "frontend", "assets", "treizeinvoice-mark-mono.svg"));

// Landing statique servie à la racine : permet de parcourir vitrine puis application
// comme un visiteur. En production elle est hébergée séparément (voir README).
var frontendPath = Path.GetFullPath(builder.Configuration["Storage:FrontendPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "..", "..", "..", "frontend"));

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));

// Les clés doivent survivre aux redéploiements, sinon chaque mise à jour
// invalide les cookies de session et les jetons antiforgery.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")))
    .SetApplicationName("TreizeInvoice");

// --- Services métier ---
builder.Services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IBusinessProfileService, BusinessProfileService>();
builder.Services.AddScoped<IClientService, ClientService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IInvoiceTotalsCalculator, KleinunternehmerTotalsCalculator>();
builder.Services.AddScoped<IInvoiceNumberGenerator, InvoiceNumberGenerator>();
builder.Services.AddScoped<IJournalService, JournalService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddSingleton(new BackupPaths(dbPath, archiveDir));
builder.Services.AddScoped<IBackupService, BackupService>();

builder.Services.AddSingleton(new PdfAssets(logoPath));
builder.Services.AddScoped<IInvoicePdfRenderer, QuestPdfInvoiceRenderer>();
builder.Services.AddSingleton<IInvoiceArchive>(new FileSystemInvoiceArchive(archiveDir));
builder.Services.AddScoped<DatabaseInitializer>();

// Derrière un reverse proxy (Caddy, nginx) : sans cela l'application se croit
// en HTTP et les cookies sécurisés seraient refusés.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

// --- Authentification par cookie (mono-utilisateur) ---
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// --- Blazor Server ---
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Applique les migrations et crée les données de départ au démarrage.
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    var seedUsername = builder.Configuration["Seed:Username"] ?? "admin";
    var generated = await initializer.InitializeAsync(seedUsername, builder.Configuration["Seed:Password"]);

    if (generated is not null)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(
            "Zugang angelegt — Benutzername: {Username} / Passwort: {Password}. " +
            "Bitte notieren und in den Einstellungen ändern.", seedUsername, generated);
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseForwardedHeaders();
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();

if (Directory.Exists(frontendPath))
{
    var landing = new PhysicalFileProvider(frontendPath);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = landing });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = landing });

    // Cloudflare Pages sert /impressum depuis impressum.html ; en local il faut le déclarer.
    foreach (var page in new[] { "impressum", "datenschutz" })
    {
        var file = Path.Combine(frontendPath, $"{page}.html");
        app.MapGet($"/{page}", () => File.Exists(file)
            ? Results.File(file, "text/html; charset=utf-8")
            : Results.NotFound());
    }
}

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

// Déconnexion : efface le cookie puis renvoie vers le login.
app.MapPost("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).DisableAntiforgery();

// Sert toujours le PDF archivé à l'émission, jamais une régénération (GoBD).
app.MapGet("/app/rechnungen/{id:int}/pdf", async (int id, IInvoiceService invoices) =>
{
    var pdf = await invoices.GetArchivedPdfAsync(id);
    return pdf is null
        ? Results.NotFound()
        : Results.File(pdf.Value.Content, "application/pdf", pdf.Value.FileName);
}).RequireAuthorization();

app.MapGet("/app/journal/export", async (int year, IJournalService journal) =>
{
    var csv = await journal.ExportCsvAsync(year);
    return Results.File(csv, "text/csv", $"journal-{year}.csv");
}).RequireAuthorization();

app.MapGet("/app/sicherung", async (IBackupService backup) =>
{
    var archive = await backup.CreateAsync();
    return Results.File(archive.Content, "application/zip", archive.FileName);
}).RequireAuthorization();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
