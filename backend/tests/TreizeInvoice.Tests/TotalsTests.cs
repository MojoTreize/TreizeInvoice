using TreizeInvoice.Domain.Entities;
using TreizeInvoice.Services.Invoicing;

namespace TreizeInvoice.Tests;

public class TotalsTests
{
    [Theory]
    // Arrondi commercial : 0,005 monte toujours (AwayFromZero), jamais l'arrondi bancaire.
    [InlineData(1, 1.005, 1.01)]
    [InlineData(1, 2.005, 2.01)]
    [InlineData(3, 0.335, 1.01)]
    [InlineData(1, 1200, 1200)]
    public void LineTotal_ArrondiCommercialA2Decimales(decimal qty, decimal price, decimal expected)
    {
        var item = new InvoiceItem { Description = "x", Quantity = qty, UnitPrice = price };
        Assert.Equal(expected, item.LineTotal);
    }

    [Fact]
    public void Kleinunternehmer_NeCalculeAucuneTva()
    {
        var invoice = new Invoice
        {
            Items =
            {
                new InvoiceItem { Description = "a", Quantity = 2, UnitPrice = 100m },
                new InvoiceItem { Description = "b", Quantity = 1, UnitPrice = 49.99m }
            }
        };

        var totals = new KleinunternehmerTotalsCalculator().Calculate(invoice);

        Assert.Equal(249.99m, totals.Net);
        Assert.Equal(0m, totals.TaxAmount);
        Assert.Equal(totals.Net, totals.Gross);
    }
}
