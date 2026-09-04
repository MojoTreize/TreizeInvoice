using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TreizeInvoice.Data;
using TreizeInvoice.Domain.Entities;
using TreizeInvoice.Domain.Enums;
using TreizeInvoice.Domain.Exceptions;
using TreizeInvoice.Services.Auditing;
using TreizeInvoice.Services.Invoicing;
using TreizeInvoice.Services.Pdf;

namespace TreizeInvoice.Tests;

public class InvoiceIssuingTests
{
    private static InvoiceService CreateService(AppDbContext ctx, string archiveRoot) =>
        new(ctx,
            new AuditService(ctx),
            new InvoiceNumberGenerator(ctx),
            new QuestPdfInvoiceRenderer(new KleinunternehmerTotalsCalculator(), new PdfAssets(null)),
            new FileSystemInvoiceArchive(archiveRoot));

    private static void SeedProfile(AppDbContext ctx)
    {
        ctx.BusinessProfiles.Add(new BusinessProfile
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
        ctx.SaveChanges();
    }

    private static Client SeedClient(AppDbContext ctx)
    {
        var client = new Client { Name = "Muster GmbH", Address = "Musterstr. 1\n10115 Berlin" };
        ctx.Clients.Add(client);
        ctx.SaveChanges();
        return client;
    }

    private static Invoice NewDraft(int clientId, int year = 2026) => new()
    {
        ClientId = clientId,
        InvoiceDate = new DateOnly(year, 3, 1),
        ServiceDate = new DateOnly(year, 2, 28),
        Items = { new InvoiceItem { Description = "Website", Quantity = 1, UnitPrice = 1200m } }
    };

    [Fact]
    public async Task Issue_AttribueLePremierNumeroDeLAnnee()
    {
        using var db = new TestDb();
        SeedProfile(db.Context);
        var client = SeedClient(db.Context);
        var service = CreateService(db.Context, db.ArchiveRoot);
        var draft = await service.CreateDraftAsync(NewDraft(client.Id));

        var issued = await service.IssueAsync(draft.Id);

        Assert.Equal("2026-0001", issued.InvoiceNumber);
        Assert.Equal(InvoiceStatus.Issued, issued.Status);
        Assert.NotNull(issued.IssuedAtUtc);
    }

    [Fact]
    public async Task Issue_SequenceContinueSansTrouNiDoublon()
    {
        using var db = new TestDb();
        SeedProfile(db.Context);
        var client = SeedClient(db.Context);
        var service = CreateService(db.Context, db.ArchiveRoot);

        var numbers = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var draft = await service.CreateDraftAsync(NewDraft(client.Id));
            numbers.Add((await service.IssueAsync(draft.Id)).InvoiceNumber!);
        }

        Assert.Equal(
            new[] { "2026-0001", "2026-0002", "2026-0003", "2026-0004", "2026-0005" },
            numbers);
        Assert.Equal(numbers.Count, numbers.Distinct().Count());
    }

    [Fact]
    public async Task Issue_SequenceRepartA1ChaqueAnnee()
    {
        using var db = new TestDb();
        SeedProfile(db.Context);
        var client = SeedClient(db.Context);
        var service = CreateService(db.Context, db.ArchiveRoot);

        var d2026 = await service.CreateDraftAsync(NewDraft(client.Id, 2026));
        var first2026 = await service.IssueAsync(d2026.Id);

        var d2027 = await service.CreateDraftAsync(NewDraft(client.Id, 2027));
        var first2027 = await service.IssueAsync(d2027.Id);

        Assert.Equal("2026-0001", first2026.InvoiceNumber);
        Assert.Equal("2027-0001", first2027.InvoiceNumber);
    }

    [Fact]
    public async Task Issue_EnConcurrence_NeProduitAucunDoublon()
    {
        // Base sur fichier : chaque tâche a sa propre connexion, comme en production.
        var dbPath = Path.Combine(Path.GetTempPath(), $"treizeinvoice-{Guid.NewGuid():N}.db");
        var archiveRoot = Path.Combine(Path.GetTempPath(), $"treizeinvoice-arch-{Guid.NewGuid():N}");
        var connectionString = $"Data Source={dbPath}";

        try
        {
            int clientId;
            using (var setup = NewContext(connectionString))
            {
                setup.Database.EnsureCreated();
                SeedProfile(setup);
                clientId = SeedClient(setup).Id;

                var seedService = CreateService(setup, archiveRoot);
                for (var i = 0; i < 10; i++)
                    await seedService.CreateDraftAsync(NewDraft(clientId));
            }

            int[] draftIds;
            using (var read = NewContext(connectionString))
                draftIds = read.Invoices.Select(i => i.Id).ToArray();

            var tasks = draftIds.Select(id => Task.Run(async () =>
            {
                // Réessaie tant que SQLite signale un verrou : le but est de vérifier
                // qu'aucun doublon n'apparaît, pas de mesurer le débit.
                for (var attempt = 0; attempt < 30; attempt++)
                {
                    try
                    {
                        using var ctx = NewContext(connectionString);
                        var service = CreateService(ctx, archiveRoot);
                        return (await service.IssueAsync(id)).InvoiceNumber!;
                    }
                    catch (SqliteException)
                    {
                        await Task.Delay(50);
                    }
                }
                throw new InvalidOperationException("Émission impossible après plusieurs tentatives.");
            })).ToArray();

            var numbers = await Task.WhenAll(tasks);

            Assert.Equal(numbers.Length, numbers.Distinct().Count());
            Assert.Equal(
                Enumerable.Range(1, numbers.Length).Select(n => $"2026-{n:0000}").OrderBy(n => n),
                numbers.OrderBy(n => n));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
            if (Directory.Exists(archiveRoot)) Directory.Delete(archiveRoot, true);
        }
    }

    [Fact]
    public async Task Issue_VerrouilleLaFacture()
    {
        using var db = new TestDb();
        SeedProfile(db.Context);
        var client = SeedClient(db.Context);
        var service = CreateService(db.Context, db.ArchiveRoot);
        var draft = await service.CreateDraftAsync(NewDraft(client.Id));

        var issued = await service.IssueAsync(draft.Id);

        await Assert.ThrowsAsync<InvoiceLockedException>(() => service.UpdateDraftAsync(issued));
        await Assert.ThrowsAsync<InvoiceLockedException>(() => service.DeleteDraftAsync(issued.Id));
        await Assert.ThrowsAsync<InvoiceLockedException>(() => service.IssueAsync(issued.Id));
    }

    [Fact]
    public async Task Issue_ArchiveLePdfEtLeRessertIdentique()
    {
        using var db = new TestDb();
        SeedProfile(db.Context);
        var client = SeedClient(db.Context);
        var service = CreateService(db.Context, db.ArchiveRoot);
        var draft = await service.CreateDraftAsync(NewDraft(client.Id));

        var issued = await service.IssueAsync(draft.Id);

        Assert.NotNull(issued.PdfPath);
        Assert.True(File.Exists(issued.PdfPath));
        Assert.Contains(Path.Combine("2026", "2026-0001.pdf"), issued.PdfPath);

        var onDisk = await File.ReadAllBytesAsync(issued.PdfPath!);
        var served = await service.GetArchivedPdfAsync(issued.Id);

        Assert.NotNull(served);
        Assert.Equal("2026-0001.pdf", served!.Value.FileName);
        Assert.Equal(onDisk, served.Value.Content);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(served.Value.Content, 0, 4));
    }

    [Fact]
    public async Task Issue_RefuseSiProfilIncomplet()
    {
        using var db = new TestDb();
        db.Context.BusinessProfiles.Add(new BusinessProfile { FullName = "Mimi" }); // sans Steuernummer ni IBAN
        db.Context.SaveChanges();
        var client = SeedClient(db.Context);
        var service = CreateService(db.Context, db.ArchiveRoot);
        var draft = await service.CreateDraftAsync(NewDraft(client.Id));

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.IssueAsync(draft.Id));

        Assert.Contains("Steuernummer", ex.Message);
        // Le compteur ne doit pas avoir été consommé : la séquence reste sans trou.
        Assert.Empty(db.Context.InvoiceNumberSequences);
    }

    [Fact]
    public async Task Issue_RefuseUneFactureSansLigne()
    {
        using var db = new TestDb();
        SeedProfile(db.Context);
        var client = SeedClient(db.Context);
        var service = CreateService(db.Context, db.ArchiveRoot);
        var draft = await service.CreateDraftAsync(new Invoice
        {
            ClientId = client.Id,
            InvoiceDate = new DateOnly(2026, 3, 1)
        });

        await Assert.ThrowsAsync<DomainException>(() => service.IssueAsync(draft.Id));
    }

    private static AppDbContext NewContext(string connectionString) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options);
}
