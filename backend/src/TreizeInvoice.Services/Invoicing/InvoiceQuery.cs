using TreizeInvoice.Domain.Enums;

namespace TreizeInvoice.Services.Invoicing;

/// <summary>Colonnes sur lesquelles la liste des factures peut être triée.</summary>
public enum InvoiceSort
{
    Date,
    Number,
    Client,
    Status,
    Amount
}

/// <summary>
/// Critères de recherche de la liste des factures. Tout est appliqué en SQL puis
/// paginé : le nombre de factures grandit indéfiniment, les charger toutes pour
/// filtrer en mémoire ne tiendrait pas.
/// </summary>
/// <param name="Text">Cherche dans le numéro et le nom du client.</param>
/// <param name="MinAmount">Borne inférieure du montant, en euros.</param>
public record InvoiceQuery(
    string? Text = null,
    InvoiceStatus? Status = null,
    DateOnly? From = null,
    DateOnly? To = null,
    decimal? MinAmount = null,
    decimal? MaxAmount = null,
    InvoiceSort Sort = InvoiceSort.Date,
    bool Descending = true,
    int Page = 1,
    int PageSize = 25);

/// <summary>Une page de résultats et le total correspondant aux critères.</summary>
public record InvoicePage<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int PageCount => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < PageCount;
}
