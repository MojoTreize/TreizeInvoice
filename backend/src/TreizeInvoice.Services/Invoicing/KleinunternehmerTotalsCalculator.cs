using TreizeInvoice.Domain.Entities;

namespace TreizeInvoice.Services.Invoicing;

/// <summary>
/// Régime Kleinunternehmer (§19 UStG) : aucune TVA n'est calculée,
/// le net est donc égal au brut. Arrondi commercial à 2 décimales.
/// </summary>
public class KleinunternehmerTotalsCalculator : IInvoiceTotalsCalculator
{
    public InvoiceTotals Calculate(Invoice invoice)
    {
        var net = Math.Round(invoice.Items.Sum(i => i.LineTotal), 2, MidpointRounding.AwayFromZero);
        return new InvoiceTotals(net, 0m, net);
    }
}
