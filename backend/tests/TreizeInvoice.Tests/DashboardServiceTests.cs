using System.IO.Compression;
using TreizeInvoice.Domain.Entities;
using TreizeInvoice.Domain.Enums;
using TreizeInvoice.Services.Auditing;
using TreizeInvoice.Services.Dashboard;
using TreizeInvoice.Services.Invoicing;
using TreizeInvoice.Services.Journal;
using TreizeInvoice.Services.Pdf;

namespace TreizeInvoice.Tests;

public class DashboardServiceTests
{
    private static readonly DateOnly Today = new(2026, 6, 15);

    private static InvoiceService Invoices(TestDb db) =>
        new(db.Context,
            new AuditService(db.Context),
            new InvoiceNumberGenerator(db.Context),
            new QuestPdfInvoiceRenderer(new KleinunternehmerTotalsCalculator(), new PdfAssets(null)),
            new FileSystemInvoiceArchive(db.ArchiveRoot));

    private static Client Seed(TestDb db)
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
        db.Context.SaveChanges();
        return client;
    }

    private static Invoice Draft(int clientId, DateOnly date, decimal amount, int termDays = 14) => new()
    {
        ClientId = clientId,
        InvoiceDate = date,
        PaymentTermDays = termDays,
        Items = { new InvoiceItem { Description = "Prestation", Quantity = 1, UnitPrice = amount } }
    };

    [Fact]
    public void Retard_EstCalculeEnJours()
    {
        var invoice = new Invoice
        {
            Status = InvoiceStatus.Issued,
            InvoiceDate = new DateOnly(2026, 5, 1),
            PaymentTermDays = 14
        };

        Assert.Equal(new DateOnly(2026, 5, 15), invoice.DueDate);
        Assert.Equal(31, invoice.DaysOverdue(Today));
        Assert.Equal(0, invoice.DaysOverdue(new DateOnly(2026, 5, 10)));
        Assert.Equal(0, invoice.DaysOverdue(new DateOnly(2026, 5, 15)));
    }

    [Fact]
    public void Retard_EstNulSiLaFactureNestPasEmise()
    {
        var paid = new Invoice
        {
            Status = InvoiceStatus.Paid,
            InvoiceDate = new DateOnly(2026, 1, 1),
            PaymentTermDays = 14
        };

        Assert.Equal(0, paid.DaysOverdue(Today));
    }

    [Fact]
    public async Task Dashboard_ListeLesImpayeesParEcheance()
    {
        using var db = new TestDb();
        var client = Seed(db);
        var service = Invoices(db);

        var late = await service.IssueAsync((await service.CreateDraftAsync(
            Draft(client.Id, new DateOnly(2026, 4, 1), 500m))).Id);
        var recent = await service.IssueAsync((await service.CreateDraftAsync(
            Draft(client.Id, new DateOnly(2026, 6, 10), 300m))).Id);

        var summary = await new DashboardService(db.Context).GetAsync(2026, Today);

        Assert.Equal(new[] { late.Id, recent.Id }, summary.Unpaid.Select(i => i.Id));
        Assert.Equal(800m, summary.UnpaidTotal);
        Assert.True(summary.Unpaid[0].DaysOverdue(Today) > 0);
        Assert.Equal(0, summary.Unpaid[1].DaysOverdue(Today));
    }

    [Fact]
    public async Task Dashboard_ExclutLesFacturesPayeesDesImpayees()
    {
        using var db = new TestDb();
        var client = Seed(db);
        var service = Invoices(db);
        var issued = await service.IssueAsync((await service.CreateDraftAsync(
            Draft(client.Id, new DateOnly(2026, 4, 1), 500m))).Id);

        await service.MarkAsPaidAsync(issued.Id, new DateOnly(2026, 4, 20));

        var summary = await new DashboardService(db.Context).GetAsync(2026, Today);

        Assert.Empty(summary.Unpaid);
        Assert.Equal(500m, summary.Collected);
        Assert.Equal(500m, summary.Invoiced);
    }

    [Fact]
    public async Task Dashboard_LeStornoNeutraliseLeChiffreDAffaires()
    {
        using var db = new TestDb();
        var client = Seed(db);
        var service = Invoices(db);
        var issued = await service.IssueAsync((await service.CreateDraftAsync(
            Draft(client.Id, new DateOnly(2026, 4, 1), 500m))).Id);

        await service.CancelAsync(issued.Id);

        var summary = await new DashboardService(db.Context).GetAsync(2026, Today);

        // L'annulée et son storno se neutralisent : contribution nulle au CA.
        Assert.Equal(0m, summary.Invoiced);
        Assert.Empty(summary.Unpaid);
    }

    [Fact]
    public async Task Dashboard_LeStornoNAffectePasLesAutresFactures()
    {
        using var db = new TestDb();
        var client = Seed(db);
        var service = Invoices(db);

        var annulee = await service.IssueAsync((await service.CreateDraftAsync(
            Draft(client.Id, new DateOnly(2026, 4, 1), 500m))).Id);
        await service.CancelAsync(annulee.Id);

        await service.IssueAsync((await service.CreateDraftAsync(
            Draft(client.Id, new DateOnly(2026, 4, 5), 250m))).Id);

        var summary = await new DashboardService(db.Context).GetAsync(2026, Today);

        Assert.Equal(250m, summary.Invoiced);
        Assert.Equal(250m, summary.UnpaidTotal);
    }

    [Fact]
    public async Task Dashboard_NeGardeQueLesCinqDernieresFactures()
    {
        using var db = new TestDb();
        var client = Seed(db);
        var service = Invoices(db);
        for (var i = 0; i < 7; i++)
            await service.CreateDraftAsync(Draft(client.Id, new DateOnly(2026, 2, 1), 100m));

        var summary = await new DashboardService(db.Context).GetAsync(2026, Today);

        Assert.Equal(5, summary.Recent.Count);
    }

    [Fact]
    public async Task Dashboard_IgnoreLesAutresAnnees()
    {
        using var db = new TestDb();
        var client = Seed(db);
        var service = Invoices(db);
        await service.IssueAsync((await service.CreateDraftAsync(
            Draft(client.Id, new DateOnly(2025, 4, 1), 900m))).Id);

        var summary = await new DashboardService(db.Context).GetAsync(2026, Today);

        Assert.Equal(0m, summary.Invoiced);
        Assert.Empty(summary.Unpaid);
    }

    [Fact]
    public async Task Dashboard_ResultatEstEncaisseMoinsDepenses()
    {
        using var db = new TestDb();
        var client = Seed(db);
        var service = Invoices(db);
        var issued = await service.IssueAsync((await service.CreateDraftAsync(
            Draft(client.Id, new DateOnly(2026, 4, 1), 1000m))).Id);
        await service.MarkAsPaidAsync(issued.Id, new DateOnly(2026, 4, 20));

        await new JournalService(db.Context, new AuditService(db.Context)).AddManualAsync(new JournalEntry
        {
            Date = new DateOnly(2026, 5, 2),
            Type = JournalEntryType.Depense,
            Amount = 250m,
            Description = "Hosting"
        });

        var summary = await new DashboardService(db.Context).GetAsync(2026, Today);

        Assert.Equal(1000m, summary.Collected);
        Assert.Equal(250m, summary.Expenses);
        Assert.Equal(750m, summary.Result);
    }
}
