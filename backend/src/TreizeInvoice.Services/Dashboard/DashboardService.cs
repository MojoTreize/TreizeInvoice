using Microsoft.EntityFrameworkCore;
using TreizeInvoice.Data;
using TreizeInvoice.Domain.Entities;
using TreizeInvoice.Domain.Enums;

namespace TreizeInvoice.Services.Dashboard;

/// <summary>Montants d'un mois : facturé (date de facture), encaissé et dépensé (journal).</summary>
public record MonthlyPoint(int Month, decimal Invoiced, decimal Revenue, decimal Expenses);

/// <summary>Nombre de factures par statut, pour l'anneau de répartition.</summary>
public record StatusCount(InvoiceStatus Status, int Count);

public record DashboardSummary(
    int Year,
    bool ProfileComplete,
    decimal Invoiced,
    decimal Collected,
    decimal Expenses,
    decimal CollectedPreviousYear,
    IReadOnlyList<Invoice> Unpaid,
    IReadOnlyList<Invoice> Recent,
    IReadOnlyList<MonthlyPoint> Monthly,
    IReadOnlyList<StatusCount> StatusCounts)
{
    public decimal UnpaidTotal => Unpaid.Sum(i => i.Total);
    public decimal Result => Collected - Expenses;

    /// <summary>Évolution des encaissements sur un an, en pourcentage. Null si l'an dernier était vide.</summary>
    public decimal? CollectedTrend => CollectedPreviousYear == 0
        ? null
        : Math.Round((Collected - CollectedPreviousYear) / CollectedPreviousYear * 100, 0);
}

public interface IDashboardService
{
    Task<DashboardSummary> GetAsync(int year, DateOnly today);
}

public class DashboardService : IDashboardService
{
    private readonly AppDbContext _db;

    public DashboardService(AppDbContext db) => _db = db;

    public async Task<DashboardSummary> GetAsync(int year, DateOnly today)
    {
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);

        var invoices = await _db.Invoices
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(i => i.Client)
            .Include(i => i.Items)
            .Where(i => i.InvoiceDate >= from && i.InvoiceDate <= to)
            .ToListAsync();

        // Une facture annulée et son storno se neutralisent : on écarte les deux,
        // sinon le montant négatif du storno serait compté une seconde fois.
        var invoiced = invoices
            .Where(i => i.Status is InvoiceStatus.Issued or InvoiceStatus.Paid)
            .Where(i => i.CancelsInvoiceId is null)
            .Sum(i => i.Total);

        var unpaid = invoices
            .Where(i => i.Status == InvoiceStatus.Issued && i.CancelsInvoiceId is null)
            .OrderBy(i => i.DueDate)
            .ToList();

        var statusCounts = new[]
            {
                InvoiceStatus.Draft, InvoiceStatus.Issued,
                InvoiceStatus.Paid, InvoiceStatus.Cancelled
            }
            .Select(s => new StatusCount(s, invoices.Count(i => i.Status == s)))
            .Where(s => s.Count > 0)
            .ToList();

        var recent = await _db.Invoices
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(i => i.Client)
            .Include(i => i.Items)
            .OrderByDescending(i => i.CreatedAtUtc)
            .ThenByDescending(i => i.Id)
            .Take(5)
            .ToListAsync();

        var journal = await _db.JournalEntries
            .AsNoTracking()
            .Where(e => e.Date >= from && e.Date <= to)
            .ToListAsync();

        var monthly = Enumerable.Range(1, 12)
            .Select(m => new MonthlyPoint(
                m,
                invoices
                    .Where(i => i.InvoiceDate.Month == m
                                && i.Status is InvoiceStatus.Issued or InvoiceStatus.Paid
                                && i.CancelsInvoiceId is null)
                    .Sum(i => i.Total),
                journal.Where(e => e.Date.Month == m && e.Type == JournalEntryType.Recette).Sum(e => e.Amount),
                journal.Where(e => e.Date.Month == m && e.Type == JournalEntryType.Depense).Sum(e => e.Amount)))
            .ToList();

        var previousFrom = new DateOnly(year - 1, 1, 1);
        var previousTo = new DateOnly(year - 1, 12, 31);
        // SQLite ne sait pas agréger des decimal : on ramène les montants puis on somme en mémoire.
        var previousAmounts = await _db.JournalEntries
            .AsNoTracking()
            .Where(e => e.Date >= previousFrom && e.Date <= previousTo && e.Type == JournalEntryType.Recette)
            .Select(e => e.Amount)
            .ToListAsync();
        var collectedPrevious = previousAmounts.Sum();

        var profile = await _db.BusinessProfiles.AsNoTracking().FirstOrDefaultAsync();

        return new DashboardSummary(
            year,
            IsComplete(profile),
            invoiced,
            journal.Where(e => e.Type == JournalEntryType.Recette).Sum(e => e.Amount),
            journal.Where(e => e.Type == JournalEntryType.Depense).Sum(e => e.Amount),
            collectedPrevious,
            unpaid,
            recent,
            monthly,
            statusCounts);
    }

    /// <summary>Mentions §14 UStG indispensables avant toute émission.</summary>
    private static bool IsComplete(BusinessProfile? p) =>
        p is not null
        && !string.IsNullOrWhiteSpace(p.FullName)
        && !string.IsNullOrWhiteSpace(p.Address)
        && !string.IsNullOrWhiteSpace(p.Steuernummer)
        && !string.IsNullOrWhiteSpace(p.Iban);
}
