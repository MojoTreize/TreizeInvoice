using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TreizeInvoice.Data;

namespace TreizeInvoice.Tests;

/// <summary>
/// Base de test : SQLite en mémoire, schéma réel (mêmes contraintes qu'en production).
/// </summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public AppDbContext Context { get; }

    public TestDb()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        Context = new AppDbContext(options);
        Context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
