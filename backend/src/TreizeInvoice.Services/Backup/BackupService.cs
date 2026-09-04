using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using TreizeInvoice.Data;

namespace TreizeInvoice.Services.Backup;

/// <summary>Emplacements sauvegardés : la base et le dossier des PDF archivés.</summary>
public record BackupPaths(string DatabasePath, string ArchiveRoot);

public interface IBackupService
{
    /// <summary>Construit une archive ZIP horodatée contenant la base et les PDF archivés.</summary>
    Task<(byte[] Content, string FileName)> CreateAsync();
}

public class BackupService : IBackupService
{
    private readonly AppDbContext _db;
    private readonly BackupPaths _paths;

    public BackupService(AppDbContext db, BackupPaths paths)
    {
        _db = db;
        _paths = paths;
    }

    public async Task<(byte[] Content, string FileName)> CreateAsync()
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var workDir = Path.Combine(Path.GetTempPath(), $"treizeinvoice-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);

        try
        {
            // VACUUM INTO produit un instantané cohérent : en mode WAL, copier
            // le fichier .db seul laisserait de côté les écritures encore en journal.
            var snapshot = Path.Combine(workDir, "treizeinvoice.db");
            await _db.Database.ExecuteSqlRawAsync("VACUUM INTO {0}", snapshot);

            var zipPath = Path.Combine(workDir, "backup.zip");
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(snapshot, "treizeinvoice.db");

                if (Directory.Exists(_paths.ArchiveRoot))
                {
                    foreach (var file in Directory.EnumerateFiles(_paths.ArchiveRoot, "*", SearchOption.AllDirectories))
                    {
                        var relative = Path.GetRelativePath(_paths.ArchiveRoot, file);
                        zip.CreateEntryFromFile(file, Path.Combine("archive", relative).Replace('\\', '/'));
                    }
                }
            }

            return (await File.ReadAllBytesAsync(zipPath), $"treizeinvoice-sauvegarde-{stamp}.zip");
        }
        finally
        {
            if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true);
        }
    }
}
