using TreizeInvoice.Data;
using TreizeInvoice.Domain.Entities;
using TreizeInvoice.Domain.Enums;
using TreizeInvoice.Services.Auditing;
using TreizeInvoice.Services.Invoicing;
using TreizeInvoice.Services.Pdf;

namespace TreizeInvoice.Tests;

/// <summary>
/// Recherche de la liste des factures. Tout doit être appliqué en SQL : avec
/// plusieurs milliers de factures, filtrer en mémoire signifierait toutes les
/// charger. Les tests vérifient donc aussi que le tri par montant est numérique,
/// point sur lequel SQLite piège (les decimal y sont stockés en TEXT).
/// </summary>
public class InvoiceSearchTests
{
    private static InvoiceService CreateService(AppDbContext ctx, string archiveRoot) =>
        new(ctx,
            new AuditService(ctx),
            new InvoiceNumberGenerator(ctx),
            new QuestPdfInvoiceRenderer(new KleinunternehmerTotalsCalculator(), new PdfAssets(null)),
            new FileSystemInvoiceArchive(archiveRoot));

    private static async Task<Invoice> SeedAsync(
        InvoiceService service, int clientId, DateOnly date, decimal amount)
    {
        var draft = new Invoice
        {
            ClientId = clientId,
            InvoiceDate = date,
            Items = new List<InvoiceItem>
            {
                new() { Description = "Leistung", Quantity = 1m, UnitPrice = amount }
            }
        };
        return await service.CreateDraftAsync(draft);
    }

    private static async Task<(TestDb Db, InvoiceService Service, int ClientA, int ClientB)> ArrangeAsync()
    {
        var db = new TestDb();
        var a = new Client { Name = "Alpha Bau GmbH", Address = "Weg 1" };
        var b = new Client { Name = "Zeta Design", Address = "Weg 2" };
        db.Context.Clients.AddRange(a, b);
        await db.Context.SaveChangesAsync();

        var service = CreateService(db.Context, db.ArchiveRoot);
        await SeedAsync(service, a.Id, new DateOnly(2026, 1, 15), 90m);
        await SeedAsync(service, a.Id, new DateOnly(2026, 3, 20), 1000m);
        await SeedAsync(service, b.Id, new DateOnly(2026, 6, 10), 250m);
        await SeedAsync(service, b.Id, new DateOnly(2025, 11, 5), 40m);

        return (db, service, a.Id, b.Id);
    }

    [Fact]
    public async Task Tri_ParMontant_EstNumeriqueEtNonAlphabetique()
    {
        var (db, service, _, _) = await ArrangeAsync();
        using var _db = db;

        var page = await service.SearchAsync(new InvoiceQuery(
            Sort: InvoiceSort.Amount, Descending: false, PageSize: 50));

        // Un tri sur du texte placerait 1000 avant 250 et 40 avant 90.
        Assert.Equal(new[] { 40m, 90m, 250m, 1000m }, page.Items.Select(i => i.Total));
    }

    [Fact]
    public async Task Filtre_ParMontant_UtiliseLesBornesInclusives()
    {
        var (db, service, _, _) = await ArrangeAsync();
        using var _db = db;

        var page = await service.SearchAsync(new InvoiceQuery(MinAmount: 90m, MaxAmount: 250m, PageSize: 50));

        Assert.Equal(2, page.TotalCount);
        Assert.All(page.Items, i => Assert.InRange(i.Total, 90m, 250m));
    }

    [Fact]
    public async Task Filtre_ParPeriode_ExclutLesAutresAnnees()
    {
        var (db, service, _, _) = await ArrangeAsync();
        using var _db = db;

        var page = await service.SearchAsync(new InvoiceQuery(
            From: new DateOnly(2026, 1, 1), To: new DateOnly(2026, 12, 31), PageSize: 50));

        Assert.Equal(3, page.TotalCount);
        Assert.All(page.Items, i => Assert.Equal(2026, i.InvoiceDate.Year));
    }

    [Fact]
    public async Task Recherche_TrouveParNomDeClient()
    {
        var (db, service, _, _) = await ArrangeAsync();
        using var _db = db;

        var page = await service.SearchAsync(new InvoiceQuery(Text: "zeta", PageSize: 50));

        Assert.Equal(2, page.TotalCount);
        Assert.All(page.Items, i => Assert.Equal("Zeta Design", i.Client.Name));
    }

    [Fact]
    public async Task Recherche_TrouveParNumeroEtIgnoreLesBrouillons()
    {
        var db = new TestDb();
        using var _db = db;
        db.Context.BusinessProfiles.Add(new BusinessProfile
        {
            FullName = "Mimi Sagno",
            Address = "Weg 3\n44805 Bochum",
            Steuernummer = "013/456/78901",
            Iban = "DE02120300000000202051",
            Bic = "BYLADEM1001",
            BankName = "Beispielbank",
            NumberFormat = "{year}-{counter:0000}"
        });
        var client = new Client { Name = "Muster GmbH", Address = "Weg 1" };
        db.Context.Clients.Add(client);
        await db.Context.SaveChangesAsync();

        var service = CreateService(db.Context, db.ArchiveRoot);
        var issued = await SeedAsync(service, client.Id, new DateOnly(2026, 2, 1), 100m);
        await service.IssueAsync(issued.Id);
        await SeedAsync(service, client.Id, new DateOnly(2026, 2, 2), 100m);

        var page = await service.SearchAsync(new InvoiceQuery(Text: "2026-0001", PageSize: 50));

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("2026-0001", page.Items[0].InvoiceNumber);
    }

    [Fact]
    public async Task Filtre_ParStatut_NeRetientQueLeStatutDemande()
    {
        var (db, service, _, _) = await ArrangeAsync();
        using var _db = db;

        var drafts = await service.SearchAsync(new InvoiceQuery(Status: InvoiceStatus.Draft, PageSize: 50));
        var paid = await service.SearchAsync(new InvoiceQuery(Status: InvoiceStatus.Paid, PageSize: 50));

        Assert.Equal(4, drafts.TotalCount);
        Assert.Equal(0, paid.TotalCount);
    }

    [Fact]
    public async Task Pagination_DecoupeSansPerdreNiRepeterUneFacture()
    {
        var (db, service, _, _) = await ArrangeAsync();
        using var _db = db;

        var first = await service.SearchAsync(new InvoiceQuery(PageSize: 3, Page: 1));
        var second = await service.SearchAsync(new InvoiceQuery(PageSize: 3, Page: 2));

        Assert.Equal(4, first.TotalCount);
        Assert.Equal(2, first.PageCount);
        Assert.Equal(3, first.Items.Count);
        Assert.Single(second.Items);
        Assert.Empty(first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)));
    }

    [Fact]
    public async Task Pagination_RameneUnePageHorsBornesDansLIntervalle()
    {
        var (db, service, _, _) = await ArrangeAsync();
        using var _db = db;

        var page = await service.SearchAsync(new InvoiceQuery(PageSize: 3, Page: 99));

        Assert.Equal(2, page.Page);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task TotalPersiste_SuitLesModificationsDuBrouillon()
    {
        var (db, service, clientA, _) = await ArrangeAsync();
        using var _db = db;

        var draft = await SeedAsync(service, clientA, new DateOnly(2026, 7, 1), 500m);
        Assert.Equal(50_000L, draft.TotalCents);

        draft.Items = new List<InvoiceItem>
        {
            new() { Description = "Andere Leistung", Quantity = 3m, UnitPrice = 133.335m }
        };
        await service.UpdateDraftAsync(draft);

        var page = await service.SearchAsync(new InvoiceQuery(MinAmount: 400m, MaxAmount: 401m, PageSize: 50));

        // 3 × 133,335 = 400,005 → 400,01 € en arrondi commercial.
        Assert.Single(page.Items);
        Assert.Equal(40_001L, page.Items[0].TotalCents);
    }
}
