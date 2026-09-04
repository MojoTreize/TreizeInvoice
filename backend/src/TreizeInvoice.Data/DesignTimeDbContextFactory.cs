using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TreizeInvoice.Data;

/// <summary>
/// Fabrique utilisée uniquement par les outils EF Core (dotnet ef) au design time.
/// Le chemin de la base importe peu ici : seul le provider (SQLite) sert à générer
/// le SQL des migrations.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=treizeinvoice_design.db")
            .Options;
        return new AppDbContext(options);
    }
}
