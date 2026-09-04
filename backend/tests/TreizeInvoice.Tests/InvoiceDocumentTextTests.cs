using TreizeInvoice.Domain.Entities;
using TreizeInvoice.Services.Invoicing;

namespace TreizeInvoice.Tests;

/// <summary>
/// L'aperçu affiché pendant la saisie et le PDF archivé lisent tous les deux
/// InvoiceDocumentText. Ces tests figent les formulations à portée juridique :
/// un aperçu qui montrerait une mention absente du document final serait pire
/// que pas d'aperçu du tout sur un outil qui vend la conformité.
/// </summary>
public class InvoiceDocumentTextTests
{
    [Fact]
    public void MentionKleinunternehmer_EstLaFormulationExacteDu19UStG()
    {
        Assert.Equal(
            "Gemäß § 19 UStG wird keine Umsatzsteuer berechnet.",
            InvoiceDocumentText.KleinunternehmerNotice);
    }

    [Fact]
    public void Titre_SansNumero_NInventePasDeNumero()
    {
        Assert.Equal("Rechnung Nr. —", InvoiceDocumentText.Title(false, null));
        Assert.Equal("Stornorechnung Nr. 2026-0002", InvoiceDocumentText.Title(true, "2026-0002"));
    }

    [Fact]
    public void Montants_SuiventLaConventionAllemande()
    {
        Assert.Equal("1.234,50 €", InvoiceDocumentText.Money(1234.5m));
        Assert.Equal("-1.200,00 €", InvoiceDocumentText.Money(-1200m));
        Assert.Equal("0,00 €", InvoiceDocumentText.Money(0m));
    }

    [Fact]
    public void Quantites_NAffichentPasDeZerosInutiles()
    {
        Assert.Equal("1", InvoiceDocumentText.Quantity(1m));
        Assert.Equal("2,5", InvoiceDocumentText.Quantity(2.5m));
        Assert.Equal("0,125", InvoiceDocumentText.Quantity(0.125m));
    }

    [Fact]
    public void Dates_SontAuFormatAllemand()
    {
        Assert.Equal("04.09.2026", InvoiceDocumentText.Date(new DateOnly(2026, 9, 4)));
    }

    [Fact]
    public void ConditionsDePaiement_CitentEcheanceEtDelai()
    {
        var text = InvoiceDocumentText.PaymentTerms(new DateOnly(2026, 9, 18), 14);

        Assert.Equal("Zahlbar ohne Abzug bis zum 18.09.2026 (14 Tage).", text);
    }

    [Fact]
    public void LigneExpediteur_MetLAdresseSurUneSeuleLigne()
    {
        var profile = new BusinessProfile
        {
            FullName = "Mimi Sagno",
            Address = "Beispielweg 3\r\n60311 Frankfurt"
        };

        Assert.Equal("Mimi Sagno · Beispielweg 3 · 60311 Frankfurt",
            InvoiceDocumentText.SenderLine(profile));
    }

    [Fact]
    public void Lignes_IgnorentLesVidesEtLesDeuxConventionsDeSautDeLigne()
    {
        Assert.Equal(
            new[] { "Musterstr. 1", "10115 Berlin" },
            InvoiceDocumentText.Lines("Musterstr. 1\r\n\r\n  10115 Berlin  \n"));
    }
}
