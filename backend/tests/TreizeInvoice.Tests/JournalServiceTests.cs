using System.Text;
using TreizeInvoice.Domain.Entities;
using TreizeInvoice.Domain.Enums;
using TreizeInvoice.Domain.Exceptions;
using TreizeInvoice.Services.Auditing;
using TreizeInvoice.Services.Invoicing;
using TreizeInvoice.Services.Journal;
using TreizeInvoice.Services.Pdf;

namespace TreizeInvoice.Tests;

public class JournalServiceTests
{
    private static JournalService CreateService(TestDb db) =>
        new(db.Context, new AuditService(db.Context));

    private static JournalEntry Expense(int year, decimal amount, string category, string description = "Achat") => new()
    {
        Date = new DateOnly(year, 5, 12),
        Type = JournalEntryType.Depense,
        Amount = amount,
        Category = category,
        Description = description
    };

    [Fact]
    public async Task AddManual_EnregistreLaDepense()
    {
        using var db = new TestDb();
        var service = CreateService(db);

        await service.AddManualAsync(Expense(2026, 49.90m, "Hosting / Domain"));

        var entry = Assert.Single(await service.GetForYearAsync(2026));
        Assert.Equal(JournalEntryType.Depense, entry.Type);
        Assert.Equal(49.90m, entry.Amount);
        Assert.Equal(-49.90m, entry.SignedAmount);
        Assert.False(entry.IsGenerated);
    }

    [Fact]
    public async Task AddManual_RefuseUnMontantNul()
    {
        using var db = new TestDb();
        var service = CreateService(db);

        await Assert.ThrowsAsync<DomainException>(
            () => service.AddManualAsync(Expense(2026, 0m, "Sonstiges")));
    }

    [Fact]
    public async Task GetForYear_NeMelangePasLesAnnees()
    {
        using var db = new TestDb();
        var service = CreateService(db);
        await service.AddManualAsync(Expense(2026, 10m, "A"));
        await service.AddManualAsync(Expense(2027, 20m, "B"));

        Assert.Single(await service.GetForYearAsync(2026));
        Assert.Equal(new[] { 2027, 2026 }, await service.GetYearsAsync());
    }

    [Fact]
    public async Task Summary_CalculeRecettesDepensesEtResultat()
    {
        using var db = new TestDb();
        var service = CreateService(db);

        db.Context.JournalEntries.Add(new JournalEntry
        {
            Date = new DateOnly(2026, 3, 20),
            Type = JournalEntryType.Recette,
            Amount = 1000m,
            Category = "Umsatz",
            Description = "Facture 2026-0001"
        });
        db.Context.SaveChanges();

        await service.AddManualAsync(Expense(2026, 250m, "Hosting / Domain"));
        await service.AddManualAsync(Expense(2026, 150m, "Bürobedarf"));

        var summary = await service.GetSummaryAsync(2026);

        Assert.Equal(1000m, summary.Revenue);
        Assert.Equal(400m, summary.Expenses);
        Assert.Equal(600m, summary.Result);
    }

    [Fact]
    public async Task Summary_RegroupeParCategorie()
    {
        using var db = new TestDb();
        var service = CreateService(db);
        await service.AddManualAsync(Expense(2026, 30m, "Bürobedarf"));
        await service.AddManualAsync(Expense(2026, 20m, "Bürobedarf"));
        await service.AddManualAsync(Expense(2026, 15m, null!));

        var summary = await service.GetSummaryAsync(2026);

        Assert.Equal(50m, summary.ByCategory.Single(c => c.Category == "Bürobedarf").Expenses);
        Assert.Equal(15m, summary.ByCategory
            .Single(c => c.Category == JournalService.UncategorizedLabel).Expenses);
    }

    [Fact]
    public async Task DeleteManual_SupprimeUneEcritureSaisieAlaMain()
    {
        using var db = new TestDb();
        var service = CreateService(db);
        await service.AddManualAsync(Expense(2026, 10m, "A"));
        var entry = (await service.GetForYearAsync(2026)).Single();

        await service.DeleteManualAsync(entry.Id);

        Assert.Empty(await service.GetForYearAsync(2026));
    }

    [Fact]
    public async Task DeleteManual_RefuseUneRecetteIssueDuneFacture()
    {
        using var db = new TestDb();
        var service = CreateService(db);
        var entryId = await SeedPaidInvoiceRevenueAsync(db);

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.DeleteManualAsync(entryId));
        Assert.Contains("facture payée", ex.Message);
    }

    // --- Export CSV ---

    [Fact]
    public async Task ExportCsv_ContientLEnteteEtLesColonnesAttendues()
    {
        using var db = new TestDb();
        var service = CreateService(db);
        await service.AddManualAsync(Expense(2026, 49.90m, "Hosting / Domain", "Domain treizeinvoice.de"));

        var csv = Decode(await service.ExportCsvAsync(2026));
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Date;Type;Catégorie;Description;Montant;N° facture", lines[0]);
        Assert.Equal("12.05.2026;Dépense;\"Hosting / Domain\";\"Domain treizeinvoice.de\";-49,90;", lines[1]);
    }

    [Fact]
    public async Task ExportCsv_UtiliseLaVirguleDecimaleEtLeSigne()
    {
        using var db = new TestDb();
        var service = CreateService(db);
        await SeedPaidInvoiceRevenueAsync(db);
        await service.AddManualAsync(Expense(2026, 1234.50m, "Sonstiges"));

        var csv = Decode(await service.ExportCsvAsync(2026));

        Assert.Contains("1200,00", csv);   // recette : positive
        Assert.Contains("-1234,50", csv);  // dépense : négative
    }

    [Fact]
    public async Task ExportCsv_ReporteLeNumeroDeFacture()
    {
        using var db = new TestDb();
        var service = CreateService(db);
        await SeedPaidInvoiceRevenueAsync(db);

        var csv = Decode(await service.ExportCsvAsync(2026));

        Assert.Contains("\"2026-0001\"", csv);
    }

    [Fact]
    public async Task ExportCsv_CommenceParLeBomUtf8()
    {
        using var db = new TestDb();
        var service = CreateService(db);
        await service.AddManualAsync(Expense(2026, 10m, "Bürobedarf"));

        var bytes = await service.ExportCsvAsync(2026);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
    }

    [Fact]
    public async Task ExportCsv_NeutraliseLInjectionDeFormule()
    {
        using var db = new TestDb();
        var service = CreateService(db);
        await service.AddManualAsync(Expense(2026, 10m, "Sonstiges", "=1+1"));

        var csv = Decode(await service.ExportCsvAsync(2026));

        Assert.Contains("\"'=1+1\"", csv);
        Assert.DoesNotContain(";\"=1+1\"", csv);
    }

    [Fact]
    public async Task ExportCsv_EchappeLesGuillemets()
    {
        using var db = new TestDb();
        var service = CreateService(db);
        await service.AddManualAsync(Expense(2026, 10m, "Sonstiges", "Écran 24\" Dell"));

        var csv = Decode(await service.ExportCsvAsync(2026));

        Assert.Contains("\"Écran 24\"\" Dell\"", csv);
    }

    private static string Decode(byte[] bytes) =>
        Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');

    /// <summary>Émet puis encaisse une facture : produit la recette automatique du journal.</summary>
    private static async Task<int> SeedPaidInvoiceRevenueAsync(TestDb db)
    {
        db.Context.BusinessProfiles.Add(new BusinessProfile
        {
            FullName = "Mimi Sagno",
            Address = "Beispielweg 3\n60311 Frankfurt",
            Steuernummer = "013/456/78901",
            Iban = "DE02120300000000202051",
            Bic = "BYLADEM1001",
            BankName = "Beispielbank",
            NumberFormat = "{year}-{counter:0000}"
        });
        var client = new Client { Name = "Muster GmbH", Address = "Musterstr. 1" };
        db.Context.Clients.Add(client);
        await db.Context.SaveChangesAsync();

        var invoices = new InvoiceService(
            db.Context,
            new AuditService(db.Context),
            new InvoiceNumberGenerator(db.Context),
            new QuestPdfInvoiceRenderer(new KleinunternehmerTotalsCalculator(), new PdfAssets(null)),
            new FileSystemInvoiceArchive(db.ArchiveRoot));

        var draft = await invoices.CreateDraftAsync(new Invoice
        {
            ClientId = client.Id,
            InvoiceDate = new DateOnly(2026, 3, 1),
            Items = { new InvoiceItem { Description = "Website", Quantity = 1, UnitPrice = 1200m } }
        });
        var issued = await invoices.IssueAsync(draft.Id);
        await invoices.MarkAsPaidAsync(issued.Id, new DateOnly(2026, 3, 20));

        return db.Context.JournalEntries.Single(e => e.InvoiceId == issued.Id).Id;
    }
}
