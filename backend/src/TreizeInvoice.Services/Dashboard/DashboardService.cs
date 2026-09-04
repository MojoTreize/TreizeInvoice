using Microsoft.EntityFrameworkCore;
using TreizeInvoice.Data;
using TreizeInvoice.Domain.Entities;
using TreizeInvoice.Domain.Enums;

namespace TreizeInvoice.Services.Dashboard;

/// <summary>Vue d'ensemble d'une année.</summary>
public record DashboardSummary(
    int Year,
    decimal Invoiced,
    decimal Collected,
    decimal Expenses,
    IReadOnlyList<Invoice> Unpaid,
    IReadOnlyList<Invoice> Recent)
{
    public decimal UnpaidTotal => Unpaid.Sum(i => i.Total);
    public decimal Result => Collected - Expenses;
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

        return new DashboardSummary(
            year,
            invoiced,
            journal.Where(e => e.Type == JournalEntryType.Recette).Sum(e => e.Amount),
            journal.Where(e => e.Type == JournalEntryType.Depense).Sum(e => e.Amount),
            unpaid,
            recent);
    }
}
