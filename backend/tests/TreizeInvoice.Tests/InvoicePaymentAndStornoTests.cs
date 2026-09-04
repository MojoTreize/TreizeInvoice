using TreizeInvoice.Domain.Entities;
using TreizeInvoice.Domain.Enums;
using TreizeInvoice.Domain.Exceptions;
using TreizeInvoice.Services.Auditing;
using TreizeInvoice.Services.Invoicing;
using TreizeInvoice.Services.Pdf;

namespace TreizeInvoice.Tests;

public class InvoicePaymentAndStornoTests
{
    private static InvoiceService CreateService(TestDb db) =>
        new(db.Context,
            new AuditService(db.Context),
            new InvoiceNumberGenerator(db.Context),
            new QuestPdfInvoiceRenderer(new KleinunternehmerTotalsCalculator(), new PdfAssets(null)),
            new FileSystemInvoiceArchive(db.ArchiveRoot));

    private static void SeedProfile(TestDb db)
    {
        db.Context.BusinessProfiles.Add(new BusinessProfile
        {
            FullName = "Mimi Sagno",
            Address = "Beispielweg 3\n60311 Frankfurt",
            Steuernummer = "013/456/78901",
            Iban = "DE02120300000000202051",
            Bic = "BYLADEM1001",
            BankName = "Beispielbank",
            IsKleinunternehmer = true,
            NumberFormat = "{year}-{counter:0000}"
        });
        db.Context.SaveChanges();
    }

    private static Client SeedClient(TestDb db)
    {
        var client = new Client { Name = "Muster GmbH", Address = "Musterstr. 1\n10115 Berlin" };
        db.Context.Clients.Add(client);
        db.Context.SaveChanges();
        return client;
    }

    private static Invoice NewDraft(int clientId) => new()
    {
        ClientId = clientId,
        InvoiceDate = new DateOnly(2026, 3, 1),
        Items =
        {
            new InvoiceItem { Description = "Website", Quantity = 1, UnitPrice = 1200m },
            new InvoiceItem { Description = "Hosting", Quantity = 12, UnitPrice = 19.90m }
        }
    };

    private static async Task<(InvoiceService Service, Invoice Issued)> IssuedInvoiceAsync(TestDb db)
    {
        SeedProfile(db);
        var client = SeedClient(db);
        var service = CreateService(db);
        var draft = await service.CreateDraftAsync(NewDraft(client.Id));
        return (service, await service.IssueAsync(draft.Id));
    }

    // --- Paiement ---

    [Fact]
    public async Task MarkAsPaid_PasseEnPayeEtCreeLaRecette()
    {
        using var db = new TestDb();
        var (service, issued) = await IssuedInvoiceAsync(db);

        await service.MarkAsPaidAsync(issued.Id, new DateOnly(2026, 3, 20));

        var reloaded = await service.GetAsync(issued.Id);
        Assert.Equal(InvoiceStatus.Paid, reloaded!.Status);
        Assert.NotNull(reloaded.PaidAtUtc);

        var entry = Assert.Single(db.Context.JournalEntries);
        Assert.Equal(JournalEntryType.Recette, entry.Type);
        Assert.Equal(1438.80m, entry.Amount);
        Assert.Equal(new DateOnly(2026, 3, 20), entry.Date);
        Assert.Equal(issued.Id, entry.InvoiceId);
        Assert.Contains("2026-0001", entry.Description);
    }

    [Fact]
    public async Task MarkAsPaid_RefuseUnBrouillon()
    {
        using var db = new TestDb();
        SeedProfile(db);
        var client = SeedClient(db);
        var service = CreateService(db);
        var draft = await service.CreateDraftAsync(NewDraft(client.Id));

        await Assert.ThrowsAsync<DomainException>(() => service.MarkAsPaidAsync(draft.Id));
        Assert.Empty(db.Context.JournalEntries);
    }

    [Fact]
    public async Task MarkAsPaid_DeuxFois_NeDoublePasLaRecette()
    {
        using var db = new TestDb();
        var (service, issued) = await IssuedInvoiceAsync(db);

        await service.MarkAsPaidAsync(issued.Id);

        await Assert.ThrowsAsync<DomainException>(() => service.MarkAsPaidAsync(issued.Id));
        Assert.Single(db.Context.JournalEntries);
    }

    // --- Storno ---

    [Fact]
    public async Task Cancel_CreeUnStornoAvecMontantsNegatifs()
    {
        using var db = new TestDb();
        var (service, issued) = await IssuedInvoiceAsync(db);
        var originalTotal = issued.Total;

        var storno = await service.CancelAsync(issued.Id);

        Assert.Equal(-originalTotal, storno.Total);
        Assert.All(storno.Items, i => Assert.True(i.LineTotal < 0));
        Assert.Equal(issued.Id, storno.CancelsInvoiceId);
    }

    [Fact]
    public async Task Cancel_AttribueUnNumeroPropreDansLaSequence()
    {
        using var db = new TestDb();
        var (service, issued) = await IssuedInvoiceAsync(db);

        var storno = await service.CancelAsync(issued.Id);

        Assert.Equal("2026-0001", issued.InvoiceNumber);
        Assert.Equal("2026-0002", storno.InvoiceNumber);
        Assert.Equal(InvoiceStatus.Issued, storno.Status);
    }

    [Fact]
    public async Task Cancel_PasseLOriginaleEnAnnuleeAvecLeLienCroise()
    {
        using var db = new TestDb();
        var (service, issued) = await IssuedInvoiceAsync(db);

        var storno = await service.CancelAsync(issued.Id);

        var original = await service.GetAsync(issued.Id);
        Assert.Equal(InvoiceStatus.Cancelled, original!.Status);
        Assert.Equal(storno.Id, original.CancelledByInvoiceId);

        var reloadedStorno = await service.GetAsync(storno.Id);
        Assert.Equal(original.Id, reloadedStorno!.CancelsInvoiceId);
    }

    [Fact]
    public async Task Cancel_ArchiveLePdfDuStorno()
    {
        using var db = new TestDb();
        var (service, issued) = await IssuedInvoiceAsync(db);

        var storno = await service.CancelAsync(issued.Id);

        Assert.NotNull(storno.PdfPath);
        Assert.True(File.Exists(storno.PdfPath));
        Assert.Contains("2026-0002.pdf", storno.PdfPath);
        // Le PDF de l'originale reste intact et distinct.
        Assert.True(File.Exists(issued.PdfPath));
        Assert.NotEqual(issued.PdfPath, storno.PdfPath);
    }

    [Fact]
    public async Task Cancel_UneFactureDejaAnnulee_EstRefuse()
    {
        using var db = new TestDb();
        var (service, issued) = await IssuedInvoiceAsync(db);
        await service.CancelAsync(issued.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.CancelAsync(issued.Id));
        Assert.Contains("bereits storniert", ex.Message);
    }

    [Fact]
    public async Task Cancel_UnBrouillon_EstRefuse()
    {
        using var db = new TestDb();
        SeedProfile(db);
        var client = SeedClient(db);
        var service = CreateService(db);
        var draft = await service.CreateDraftAsync(NewDraft(client.Id));

        await Assert.ThrowsAsync<DomainException>(() => service.CancelAsync(draft.Id));
    }

    [Fact]
    public async Task Cancel_UneFacturePayee_EstRefuse()
    {
        using var db = new TestDb();
        var (service, issued) = await IssuedInvoiceAsync(db);
        await service.MarkAsPaidAsync(issued.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.CancelAsync(issued.Id));
        Assert.Contains("vereinnahmt", ex.Message);
    }

    [Fact]
    public async Task Cancel_LaissePossibleUneNouvelleFactureCorrigee()
    {
        using var db = new TestDb();
        var (service, issued) = await IssuedInvoiceAsync(db);
        await service.CancelAsync(issued.Id);

        var corrected = await service.CreateDraftAsync(NewDraft(issued.ClientId));
        var reissued = await service.IssueAsync(corrected.Id);

        Assert.Equal("2026-0003", reissued.InvoiceNumber);
    }

    [Fact]
    public async Task Cancel_UnStorno_EstRefuse()
    {
        using var db = new TestDb();
        var (service, issued) = await IssuedInvoiceAsync(db);
        var storno = await service.CancelAsync(issued.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.CancelAsync(storno.Id));
        Assert.Contains("selbst eine Stornorechnung", ex.Message);
    }

    [Fact]
    public async Task MarkAsPaid_UnStorno_EstRefuse()
    {
        using var db = new TestDb();
        var (service, issued) = await IssuedInvoiceAsync(db);
        var storno = await service.CancelAsync(issued.Id);

        await Assert.ThrowsAsync<DomainException>(() => service.MarkAsPaidAsync(storno.Id));
        Assert.Empty(db.Context.JournalEntries);
    }

    [Fact]
    public async Task Cancel_EcritLesDeuxLignesDAudit()
    {
        using var db = new TestDb();
        var (service, issued) = await IssuedInvoiceAsync(db);

        var storno = await service.CancelAsync(issued.Id);

        Assert.Contains(db.Context.AuditLogs, a => a.EntityId == issued.Id && a.Action == "Cancelled");
        Assert.Contains(db.Context.AuditLogs, a => a.EntityId == storno.Id && a.Action == "StornoIssued");
    }
}
