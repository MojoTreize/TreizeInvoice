using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TreizeInvoice.Data;
using TreizeInvoice.Domain.Entities;
using TreizeInvoice.Domain.Enums;
using TreizeInvoice.Domain.Exceptions;
using TreizeInvoice.Services.Auditing;

namespace TreizeInvoice.Services.Journal;

public class JournalService : IJournalService
{
    public const string UncategorizedLabel = "Ohne Kategorie";

    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private readonly AppDbContext _db;
    private readonly IAuditService _audit;

    public JournalService(AppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<List<JournalEntry>> GetForYearAsync(int year) =>
        await QueryYear(year)
            .AsNoTracking()
            .Include(e => e.Invoice)
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.Id)
            .ToListAsync();

    public async Task<List<int>> GetYearsAsync()
    {
        var dates = await _db.JournalEntries.Select(e => e.Date).ToListAsync();
        return dates.Select(d => d.Year).Distinct().OrderByDescending(y => y).ToList();
    }

    public async Task<JournalSummary> GetSummaryAsync(int year)
    {
        var entries = await QueryYear(year).AsNoTracking().ToListAsync();

        var byCategory = entries
            .GroupBy(e => string.IsNullOrWhiteSpace(e.Category) ? UncategorizedLabel : e.Category!)
            .Select(g => new CategoryTotal(
                g.Key,
                g.Where(e => e.Type == JournalEntryType.Recette).Sum(e => e.Amount),
                g.Where(e => e.Type == JournalEntryType.Depense).Sum(e => e.Amount)))
            .OrderByDescending(c => c.Revenue + c.Expenses)
            .ToList();

        return new JournalSummary(
            entries.Where(e => e.Type == JournalEntryType.Recette).Sum(e => e.Amount),
            entries.Where(e => e.Type == JournalEntryType.Depense).Sum(e => e.Amount),
            byCategory);
    }

    public async Task AddManualAsync(JournalEntry entry)
    {
        if (entry.Amount <= 0)
            throw new DomainException("Der Betrag muss größer als 0 sein.");

        if (string.IsNullOrWhiteSpace(entry.Description))
            throw new DomainException("Bitte geben Sie eine Beschreibung an.");

        entry.InvoiceId = null; // une écriture manuelle n'est jamais rattachée à une facture
        _db.JournalEntries.Add(entry);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(nameof(JournalEntry), entry.Id, "ManualEntryCreated",
            new { entry.Date, entry.Type, entry.Amount, entry.Category });
    }

    public async Task DeleteManualAsync(int id)
    {
        var entry = await _db.JournalEntries.FirstOrDefaultAsync(e => e.Id == id);
        if (entry is null) return;

        // Les recettes issues d'une facture suivent le sort de la facture (GoBD).
        if (entry.InvoiceId is not null)
            throw new DomainException(
                "Diese Einnahme stammt aus einer bezahlten Rechnung und kann nicht von Hand gelöscht werden.");

        _db.JournalEntries.Remove(entry);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(nameof(JournalEntry), id, "ManualEntryDeleted");
    }

    public async Task<byte[]> ExportCsvAsync(int year)
    {
        var entries = await QueryYear(year)
            .AsNoTracking()
            .Include(e => e.Invoice)
            .OrderBy(e => e.Date)
            .ThenBy(e => e.Id)
            .ToListAsync();

        var csv = new StringBuilder();
        csv.Append("Datum;Art;Kategorie;Beschreibung;Betrag;Rechnungsnummer\r\n");

        foreach (var e in entries)
        {
            csv.Append(e.Date.ToString("dd.MM.yyyy", De)).Append(';')
               .Append(e.Type == JournalEntryType.Recette ? "Einnahme" : "Ausgabe").Append(';')
               .Append(Field(e.Category)).Append(';')
               .Append(Field(e.Description)).Append(';')
               .Append(e.SignedAmount.ToString("0.00", De)).Append(';')
               .Append(Field(e.Invoice?.InvoiceNumber))
               .Append("\r\n");
        }

        // BOM explicite : GetBytes n'écrit jamais le préambule, et sans lui
        // Excel en allemand casse les accents.
        return Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(csv.ToString()))
            .ToArray();
    }

    private IQueryable<JournalEntry> QueryYear(int year) =>
        _db.JournalEntries.Where(e =>
            e.Date >= new DateOnly(year, 1, 1) && e.Date <= new DateOnly(year, 12, 31));

    /// <summary>
    /// Échappe un champ CSV. Le préfixe apostrophe neutralise l'injection de formules :
    /// un texte commençant par =, +, - ou @ serait sinon exécuté par Excel.
    /// </summary>
    private static string Field(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var text = value;
        if ("=+-@\t\r".Contains(text[0]))
            text = "'" + text;

        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
