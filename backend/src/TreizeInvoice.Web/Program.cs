using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using TreizeInvoice.Data;
using TreizeInvoice.Services.Auditing;
using TreizeInvoice.Services.Auth;
using TreizeInvoice.Services.Clients;
using TreizeInvoice.Services.Invoicing;
using TreizeInvoice.Services.Security;
using TreizeInvoice.Services.Settings;
using TreizeInvoice.Services.Setup;
using TreizeInvoice.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// --- Base de données SQLite (fichier dans backend/data) ---
var dataDir = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "data"));
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "treizeinvoice.db");
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));

// --- Services métier ---
builder.Services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IBusinessProfileService, BusinessProfileService>();
builder.Services.AddScoped<IClientService, ClientService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IInvoiceTotalsCalculator, KleinunternehmerTotalsCalculator>();
builder.Services.AddScoped<DatabaseInitializer>();

// --- Authentification par cookie (mono-utilisateur) ---
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
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
            "Compte initial créé — identifiant : {Username} / mot de passe : {Password}. " +
            "Notez-le puis changez-le dans Paramètres.", seedUsername, generated);
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

// Déconnexion : efface le cookie puis renvoie vers le login.
app.MapPost("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).DisableAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
