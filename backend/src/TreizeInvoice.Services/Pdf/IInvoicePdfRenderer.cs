using TreizeInvoice.Domain.Entities;

namespace TreizeInvoice.Services.Pdf;

/// <summary>
/// Rendu du document de facture. Derrière une interface pour qu'un autre format
/// (XRechnung / ZUGFeRD) puisse être ajouté sans toucher au workflow d'émission.
/// </summary>
public interface IInvoicePdfRenderer
{
    /// <summary>Génère le PDF allemand conforme §14 UStG.</summary>
    byte[] Render(Invoice invoice, BusinessProfile profile);
}
