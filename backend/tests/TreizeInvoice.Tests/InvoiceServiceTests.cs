using TreizeInvoice.Domain.Entities;
using TreizeInvoice.Domain.Enums;
using TreizeInvoice.Domain.Exceptions;
using TreizeInvoice.Services.Auditing;
using TreizeInvoice.Services.Invoicing;
using TreizeInvoice.Services.Pdf;

namespace TreizeInvoice.Tests;

public class InvoiceServiceTests
{
    private static InvoiceService CreateService(TestDb db) =>
        new(db.Context,
            new AuditService(db.Context),
            new InvoiceNumberGenerator(db.Context),
            new QuestPdfInvoiceRenderer(new KleinunternehmerTotalsCalculator(), new PdfAssets(null)),
            new FileSystemInvoiceArchive(db.ArchiveRoot));

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
            new InvoiceItem { Description = "Website", Quantity = 1, UnitPrice = 1200m }
        }
    };

    [Fact]
    public async Task CreateDraft_NAttribuePasDeNumero()
    {
        using var db = new TestDb();
        var client = SeedClient(db);
        var service = CreateService(db);

        var draft = await service.CreateDraftAsync(NewDraft(client.Id));

        Assert.Equal(InvoiceStatus.Draft, draft.Status);
        Assert.Null(draft.InvoiceNumber);
        Assert.Null(draft.IssuedAtUtc);
    }

    [Fact]
    public async Task CreateDraft_EcritUneLigneDAudit()
    {
        using var db = new TestDb();
        var client = SeedClient(db);
        var service = CreateService(db);

        var draft = await service.CreateDraftAsync(NewDraft(client.Id));

        Assert.Contains(db.Context.AuditLogs,
            a => a.Entity == nameof(Invoice) && a.EntityId == draft.Id && a.Action == "DraftCreated");
    }

    [Fact]
    public async Task UpdateDraft_ModifieLesLignes()
    {
        using var db = new TestDb();
        var client = SeedClient(db);
        var service = CreateService(db);
        var draft = await service.CreateDraftAsync(NewDraft(client.Id));

        var toUpdate = await service.GetAsync(draft.Id);
        toUpdate!.Items.Add(new InvoiceItem { Description = "Hosting", Quantity = 12, UnitPrice = 10m });
        await service.UpdateDraftAsync(toUpdate);

        var reloaded = await service.GetAsync(draft.Id);
        Assert.Equal(2, reloaded!.Items.Count);
        Assert.Equal(1320m, reloaded.Total);
    }

    [Fact]
    public async Task UpdateDraft_SurFactureEmise_Leve_InvoiceLockedException()
    {
        using var db = new TestDb();
        var client = SeedClient(db);
        var service = CreateService(db);
        var draft = await service.CreateDraftAsync(NewDraft(client.Id));

        // Simule l'émission (le workflow complet arrive au Bloc 3).
        var issued = await service.GetAsync(draft.Id);
        issued!.Status = InvoiceStatus.Issued;
        issued.InvoiceNumber = "2026-0001";
        await db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvoiceLockedException>(() => service.UpdateDraftAsync(issued));
    }

    [Fact]
    public async Task DeleteDraft_SurFactureEmise_Leve_InvoiceLockedException()
    {
        using var db = new TestDb();
        var client = SeedClient(db);
        var service = CreateService(db);
        var draft = await service.CreateDraftAsync(NewDraft(client.Id));

        var issued = await service.GetAsync(draft.Id);
        issued!.Status = InvoiceStatus.Issued;
        issued.InvoiceNumber = "2026-0001";
        await db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvoiceLockedException>(() => service.DeleteDraftAsync(draft.Id));
    }

    [Fact]
    public async Task DeleteDraft_SupprimeLeBrouillon()
    {
        using var db = new TestDb();
        var client = SeedClient(db);
        var service = CreateService(db);
        var draft = await service.CreateDraftAsync(NewDraft(client.Id));

        await service.DeleteDraftAsync(draft.Id);

        Assert.Null(await service.GetAsync(draft.Id));
    }

    [Fact]
    public async Task Duplicate_CreeUnNouveauBrouillonSansNumero()
    {
        using var db = new TestDb();
        var client = SeedClient(db);
        var service = CreateService(db);
        var source = await service.CreateDraftAsync(NewDraft(client.Id));

        var copy = await service.DuplicateAsDraftAsync(source.Id);

        Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal(InvoiceStatus.Draft, copy.Status);
        Assert.Null(copy.InvoiceNumber);
        Assert.Single(copy.Items);
        Assert.Equal(source.Total, copy.Total);
    }
}
