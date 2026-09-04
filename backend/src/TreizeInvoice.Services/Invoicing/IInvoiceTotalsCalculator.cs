using TreizeInvoice.Domain.Entities;

namespace TreizeInvoice.Services.Invoicing;

/// <summary>Totaux d'une facture. La TVA est isolée ici pour pouvoir sortir du régime §19 plus tard.</summary>
public record InvoiceTotals(decimal Net, decimal TaxAmount, decimal Gross);

/// <summary>
/// Calcul des totaux. Passe par une interface pour qu'une implémentation
/// assujettie à la TVA puisse être ajoutée sans modifier le reste du code.
/// </summary>
public interface IInvoiceTotalsCalculator
{
    InvoiceTotals Calculate(Invoice invoice);
}
