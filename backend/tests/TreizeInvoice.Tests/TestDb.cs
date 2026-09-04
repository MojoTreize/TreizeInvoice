using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TreizeInvoice.Data;

namespace TreizeInvoice.Tests;

/// <summary>
/// Base de test : SQLite en mémoire, schéma réel (mêmes contraintes qu'en production).
/// Fournit aussi un dossier d'archive temporaire pour les PDF.
/// </summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public AppDbContext Context { get; }

    /// <summary>Dossier temporaire tenant lieu de backend/data/archive.</summary>
    public string ArchiveRoot { get; }

    public TestDb()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        Context = new AppDbContext(options);
        Context.Database.EnsureCreated();

        ArchiveRoot = Path.Combine(Path.GetTempPath(), "treizeinvoice-tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();

        if (Directory.Exists(ArchiveRoot))
            Directory.Delete(ArchiveRoot, recursive: true);
    }
}
