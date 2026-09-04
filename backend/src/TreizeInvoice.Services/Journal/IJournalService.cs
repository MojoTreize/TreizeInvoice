using TreizeInvoice.Domain.Entities;

namespace TreizeInvoice.Services.Journal;

/// <summary>Totaux d'une année, base de la déclaration EÜR.</summary>
public record JournalSummary(
    decimal Revenue,
    decimal Expenses,
    IReadOnlyList<CategoryTotal> ByCategory)
{
    /// <summary>Résultat de l'exercice (recettes − dépenses).</summary>
    public decimal Result => Revenue - Expenses;
}

public record CategoryTotal(string Category, decimal Revenue, decimal Expenses);

public interface IJournalService
{
    /// <summary>Écritures d'une année, les plus récentes d'abord.</summary>
    Task<List<JournalEntry>> GetForYearAsync(int year);

    /// <summary>Années comportant au moins une écriture, décroissantes.</summary>
    Task<List<int>> GetYearsAsync();

    Task<JournalSummary> GetSummaryAsync(int year);

    /// <summary>Ajoute une écriture saisie à la main (dépense ou recette hors facture).</summary>
    Task AddManualAsync(JournalEntry entry);

    /// <summary>Supprime une écriture manuelle. Refuse celles générées par une facture.</summary>
    Task DeleteManualAsync(int id);

    /// <summary>Export CSV de l'année, prêt pour le conseiller fiscal.</summary>
    Task<byte[]> ExportCsvAsync(int year);
}
