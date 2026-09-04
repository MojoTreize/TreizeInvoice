using TreizeInvoice.Domain.Entities;
using TreizeInvoice.Domain.Enums;
using TreizeInvoice.Services.Backup;
using TreizeInvoice.Services.Compliance;

namespace TreizeInvoice.Tests;

/// <summary>
/// La Verfahrensdokumentation n'a de valeur devant un vérificateur que si elle
/// décrit l'installation réelle. Ces tests vérifient donc surtout que les
/// chiffres cités proviennent bien de la base, pas d'un texte type.
/// </summary>
public class VerfahrensdokumentationTests
{
    private static Invoice Issued(string number, int year) => new()
    {
        ClientId = 1,
        InvoiceNumber = number,
        InvoiceDate = new DateOnly(year, 3, 14),
        Status = InvoiceStatus.Issued
    };

    private static VerfahrensdokumentationService Service(TestDb db) =>
        new(db.Context, new BackupPaths(@"C:\daten\treizeinvoice.db", db.ArchiveRoot));

    [Fact]
    public async Task Collect_ReprendLeProfilEtLesCheminsReels()
    {
        using var db = new TestDb();
        db.Context.BusinessProfiles.Add(new BusinessProfile
        {
            FullName = "Atelier Sonnenhof",
            Address = "Lindenstr. 14\r\n10969 Berlin",
            Steuernummer = "30/123/45678",
            IsKleinunternehmer = true,
            NumberFormat = "RE-{year}-{counter:000}"
        });
        await db.Context.SaveChangesAsync();

        var facts = await Service(db).CollectAsync();

        Assert.Equal("Atelier Sonnenhof", facts.OwnerName);
        Assert.Equal("30/123/45678", facts.Steuernummer);
        Assert.True(facts.IsKleinunternehmer);
        Assert.Equal("RE-{year}-{counter:000}", facts.NumberFormat);
        Assert.Equal(@"C:\daten\treizeinvoice.db", facts.DatabasePath);
        Assert.Equal(db.ArchiveRoot, facts.ArchivePath);
    }

    [Fact]
    public async Task Collect_SansProfil_NeJettePasEtUtiliseLeFormatParDefaut()
    {
        using var db = new TestDb();

        var facts = await Service(db).CollectAsync();

        Assert.Equal(string.Empty, facts.OwnerName);
        Assert.Equal("{year}-{counter:0000}", facts.NumberFormat);
        Assert.Empty(facts.Ranges);
    }

    [Fact]
    public async Task Collect_ComptePlagesDeNumerosParAnnee()
    {
        using var db = new TestDb();
        db.Context.Clients.Add(new Client { Name = "Kramer GmbH", Address = "Hauptstr. 3" });
        db.Context.Invoices.AddRange(
            Issued("2025-0001", 2025),
            Issued("2025-0002", 2025),
            Issued("2026-0001", 2026),
            Issued("2026-0002", 2026),
            Issued("2026-0003", 2026));
        // Un brouillon n'a pas de numéro : il ne doit apparaître dans aucune plage.
        db.Context.Invoices.Add(new Invoice
        {
            ClientId = 1,
            InvoiceDate = new DateOnly(2026, 5, 1),
            Status = InvoiceStatus.Draft
        });
        await db.Context.SaveChangesAsync();

        var facts = await Service(db).CollectAsync();

        Assert.Equal(2, facts.Ranges.Count);

        var y2025 = facts.Ranges[0];
        Assert.Equal(2025, y2025.Year);
        Assert.Equal(2, y2025.IssuedCount);
        Assert.Equal("2025-0001", y2025.First);
        Assert.Equal("2025-0002", y2025.Last);

        var y2026 = facts.Ranges[1];
        Assert.Equal(2026, y2026.Year);
        Assert.Equal(3, y2026.IssuedCount);
        Assert.Equal("2026-0001", y2026.First);
        Assert.Equal("2026-0003", y2026.Last);

        Assert.Equal(1, facts.DraftCount);
    }

    [Fact]
    public async Task Collect_VentileLesStatutsEtIsoleLesStorno()
    {
        using var db = new TestDb();
        db.Context.Clients.Add(new Client { Name = "Kramer GmbH", Address = "Hauptstr. 3" });

        var paid = Issued("2026-0001", 2026);
        paid.Status = InvoiceStatus.Paid;

        var cancelled = Issued("2026-0002", 2026);
        cancelled.Status = InvoiceStatus.Cancelled;

        db.Context.Invoices.AddRange(paid, cancelled);
        await db.Context.SaveChangesAsync();

        // Le storno doit pointer sur une facture existante (contrainte FK réelle).
        var storno = Issued("2026-0003", 2026);
        storno.CancelsInvoiceId = cancelled.Id;
        db.Context.Invoices.Add(storno);

        db.Context.JournalEntries.Add(new JournalEntry
        {
            Date = new DateOnly(2026, 3, 14),
            Description = "Beratung",
            Amount = 100m
        });
        await db.Context.SaveChangesAsync();

        var facts = await Service(db).CollectAsync();

        Assert.Equal(1, facts.PaidCount);
        Assert.Equal(1, facts.CancelledCount);
        Assert.Equal(1, facts.IssuedCount);
        Assert.Equal(1, facts.StornoCount);
        Assert.Equal(0, facts.DraftCount);
        Assert.Equal(1, facts.JournalEntryCount);
    }

    [Fact]
    public async Task Collect_RestitueLaPeriodeDuJournalDAudit()
    {
        using var db = new TestDb();
        db.Context.AuditLogs.AddRange(
            new AuditLog { TimestampUtc = new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc), Entity = "Invoice", Action = "Created" },
            new AuditLog { TimestampUtc = new DateTime(2026, 4, 2, 17, 0, 0, DateTimeKind.Utc), Entity = "Invoice", Action = "Issued" });
        await db.Context.SaveChangesAsync();

        var facts = await Service(db).CollectAsync();

        Assert.Equal(2, facts.AuditEntryCount);
        Assert.Equal(new DateTime(2026, 1, 5, 9, 0, 0), facts.FirstAuditUtc);
        Assert.Equal(new DateTime(2026, 4, 2, 17, 0, 0), facts.LastAuditUtc);
    }

    [Fact]
    public async Task Create_ProduitUnPdfNommeAvecLaDate()
    {
        using var db = new TestDb();
        db.Context.BusinessProfiles.Add(new BusinessProfile
        {
            FullName = "Atelier Sonnenhof",
            Address = "Lindenstr. 14",
            Steuernummer = "30/123/45678"
        });
        db.Context.Clients.Add(new Client { Name = "Kramer GmbH", Address = "Hauptstr. 3" });
        db.Context.Invoices.Add(Issued("2026-0001", 2026));
        await db.Context.SaveChangesAsync();

        var (fileName, content) = await Service(db).CreateAsync();

        Assert.Equal($"verfahrensdokumentation-{DateTime.Now:yyyyMMdd}.pdf", fileName);
        Assert.True(content.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(content, 0, 4));
    }

    [Fact]
    public void Render_FonctionneMemeSansAucuneDonnee()
    {
        var facts = new VerfahrensdokumentationFacts(
            CreatedAt: new DateTime(2026, 3, 14, 10, 0, 0),
            OwnerName: "",
            OwnerAddress: "",
            Steuernummer: "",
            IsKleinunternehmer: true,
            NumberFormat: "{year}-{counter:0000}",
            DatabasePath: "data/treizeinvoice.db",
            ArchivePath: "data/archive",
            DraftCount: 0, IssuedCount: 0, PaidCount: 0, CancelledCount: 0, StornoCount: 0,
            Ranges: Array.Empty<NumberRange>(),
            JournalEntryCount: 0,
            AuditEntryCount: 0,
            FirstAuditUtc: null,
            LastAuditUtc: null);

        var pdf = VerfahrensdokumentationService.Render(facts);

        Assert.True(pdf.Length > 1000);
    }
}
