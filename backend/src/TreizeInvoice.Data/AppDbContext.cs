using Microsoft.EntityFrameworkCore;
using TreizeInvoice.Domain.Entities;

namespace TreizeInvoice.Data;

/// <summary>
/// Contexte EF Core (SQLite). Toutes les contraintes légales structurantes
/// (précision decimal, unicité du numéro de facture, soft delete client)
/// sont déclarées ici via la configuration Fluent.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<BusinessProfile> BusinessProfiles => Set<BusinessProfile>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<InvoiceNumberSequence> InvoiceNumberSequences => Set<InvoiceNumberSequence>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.Username).IsRequired().HasMaxLength(100);
            e.Property(u => u.PasswordHash).IsRequired();
        });

        modelBuilder.Entity<BusinessProfile>(e =>
        {
            e.Property(p => p.FullName).HasMaxLength(200);
            e.Property(p => p.NumberFormat).HasMaxLength(50);
        });

        modelBuilder.Entity<Client>(e =>
        {
            e.Property(c => c.Name).IsRequired().HasMaxLength(200);
            // Soft delete : les clients supprimés sont masqués par défaut.
            e.HasQueryFilter(c => !c.IsDeleted);
            e.HasMany(c => c.Invoices)
                .WithOne(i => i.Client)
                .HasForeignKey(i => i.ClientId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Invoice>(e =>
        {
            e.Property(i => i.InvoiceNumber).HasMaxLength(30);
            // Un numéro émis est unique ; les brouillons (null) sont exclus de la contrainte.
            e.HasIndex(i => i.InvoiceNumber)
                .IsUnique()
                .HasFilter("[InvoiceNumber] IS NOT NULL");
            e.HasMany(i => i.Items)
                .WithOne(it => it.Invoice)
                .HasForeignKey(it => it.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.CancelsInvoice)
                .WithMany()
                .HasForeignKey(i => i.CancelsInvoiceId)
                .OnDelete(DeleteBehavior.Restrict);
            e.Ignore(i => i.Total);
        });

        modelBuilder.Entity<InvoiceItem>(e =>
        {
            e.Property(it => it.Description).IsRequired().HasMaxLength(500);
            e.Property(it => it.Quantity).HasPrecision(18, 3);
            e.Property(it => it.UnitPrice).HasPrecision(18, 2);
            e.Ignore(it => it.LineTotal);
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.Property(a => a.Entity).IsRequired().HasMaxLength(100);
            e.Property(a => a.Action).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<InvoiceNumberSequence>(e =>
        {
            e.HasKey(s => s.Year);
            // L'année est une donnée métier, pas une clé générée par la base.
            e.Property(s => s.Year).ValueGeneratedNever();
        });

        modelBuilder.Entity<JournalEntry>(e =>
        {
            e.Property(j => j.Amount).HasPrecision(18, 2);
            e.Property(j => j.Description).IsRequired().HasMaxLength(500);
            e.Property(j => j.Category).HasMaxLength(100);
            e.Ignore(j => j.IsGenerated);
            e.Ignore(j => j.SignedAmount);
            e.HasOne(j => j.Invoice)
                .WithMany()
                .HasForeignKey(j => j.InvoiceId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
