namespace TreizeInvoice.Domain.Enums;

/// <summary>
/// Cycle de vie d'une facture. Transitions autorisées :
/// Draft → Issued, Issued → Paid, Issued → Cancelled.
/// Une facture Issued est immuable (GoBD).
/// </summary>
public enum InvoiceStatus
{
    Draft = 0,
    Issued = 1,
    Paid = 2,
    Cancelled = 3
}
