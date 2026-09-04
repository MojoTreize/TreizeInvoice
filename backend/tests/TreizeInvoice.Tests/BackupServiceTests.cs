using System.IO.Compression;
using TreizeInvoice.Domain.Entities;
using TreizeInvoice.Services.Backup;

namespace TreizeInvoice.Tests;

public class BackupServiceTests
{
    private static void SeedArchive(string archiveRoot)
    {
        var dir = Path.Combine(archiveRoot, "2026");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "2026-0001.pdf"), new byte[] { 0x25, 0x50, 0x44, 0x46 });
        File.WriteAllBytes(Path.Combine(dir, "2026-0002.pdf"), new byte[] { 0x25, 0x50, 0x44, 0x46 });
    }

    [Fact]
    public async Task Create_ProduitUnZipHorodateAvecLaBaseEtLesPdf()
    {
        using var db = new TestDb();
        db.Context.Clients.Add(new Client { Name = "Muster GmbH", Address = "Musterstr. 1" });
        await db.Context.SaveChangesAsync();
        SeedArchive(db.ArchiveRoot);

        var service = new BackupService(db.Context, new BackupPaths("ignored.db", db.ArchiveRoot));
        var (content, fileName) = await service.CreateAsync();

        Assert.StartsWith("treizeinvoice-sauvegarde-", fileName);
        Assert.EndsWith(".zip", fileName);

        using var zip = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.FullName).ToList();

        Assert.Contains("treizeinvoice.db", names);
        Assert.Contains("archive/2026/2026-0001.pdf", names);
        Assert.Contains("archive/2026/2026-0002.pdf", names);
    }

    [Fact]
    public async Task Create_LeSnapshotEstUneBaseSqliteLisible()
    {
        using var db = new TestDb();
        db.Context.Clients.Add(new Client { Name = "Muster GmbH", Address = "Musterstr. 1" });
        await db.Context.SaveChangesAsync();

        var service = new BackupService(db.Context, new BackupPaths("ignored.db", db.ArchiveRoot));
        var (content, _) = await service.CreateAsync();

        using var zip = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
        var entry = zip.GetEntry("treizeinvoice.db")!;

        using var stream = entry.Open();
        var header = new byte[16];
        stream.ReadExactly(header);

        // Un fichier SQLite commence toujours par "SQLite format 3\0".
        Assert.Equal("SQLite format 3\0", System.Text.Encoding.ASCII.GetString(header));
        Assert.True(entry.Length > 0);
    }

    [Fact]
    public async Task Create_FonctionneSansDossierArchive()
    {
        using var db = new TestDb();
        var service = new BackupService(db.Context, new BackupPaths("ignored.db", db.ArchiveRoot));

        var (content, _) = await service.CreateAsync();

        using var zip = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
        Assert.Single(zip.Entries);
    }
}
