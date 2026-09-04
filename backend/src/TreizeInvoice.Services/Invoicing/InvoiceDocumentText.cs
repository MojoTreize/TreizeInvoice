using System.Globalization;
using TreizeInvoice.Domain.Entities;

namespace TreizeInvoice.Services.Invoicing;

/// <summary>
/// Textes du document de facture, en un seul endroit.
///
/// Le PDF archivé et l'aperçu affiché pendant la saisie doivent dire exactement la
/// même chose : un aperçu qui promettrait une mention absente du PDF serait pire
/// que pas d'aperçu du tout sur un outil qui vend la conformité. Partager les
/// chaînes rend la dérive impossible sur ce qui a une portée juridique.
/// </summary>
public static class InvoiceDocumentText
{
    public static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Mention §19 UStG. Formulation exacte, ne pas paraphraser.</summary>
    public const string KleinunternehmerNotice = "Gemäß § 19 UStG wird keine Umsatzsteuer berechnet.";

    public const string PositionHeader = "Pos.";
    public const string DescriptionHeader = "Beschreibung";
    public const string QuantityHeader = "Menge";
    public const string UnitPriceHeader = "Einzelpreis";
    public const string AmountHeader = "Betrag";
    public const string TotalLabel = "Gesamtbetrag";
    public const string BankDetailsLabel = "Bankverbindung";

    public const string InvoiceNumberLabel = "Rechnungsnummer";
    public const string InvoiceDateLabel = "Rechnungsdatum";
    public const string ServiceDateLabel = "Leistungsdatum";
    public const string ServicePeriodLabel = "Leistungszeitraum";
    public const string TaxNumberLabel = "Steuernummer";

    /// <summary>Titre du document. Le numéro est absent tant que la facture est un brouillon.</summary>
    public static string Title(bool isStorno, string? invoiceNumber) =>
        $"{(isStorno ? "Stornorechnung" : "Rechnung")} Nr. {invoiceNumber ?? "—"}";

    public static string StornoReference(string? cancelledNumber, DateOnly cancelledDate) =>
        $"Storno zu Rechnung Nr. {cancelledNumber ?? "—"} vom {Date(cancelledDate)}";

    public static string PaymentTerms(DateOnly dueDate, int paymentTermDays) =>
        $"Zahlbar ohne Abzug bis zum {Date(dueDate)} ({paymentTermDays} Tage).";

    public static string SenderLine(BusinessProfile profile) =>
        $"{profile.FullName} · {OneLine(profile.Address)}";

    public static string Date(DateOnly value) => value.ToString("dd.MM.yyyy", De);

    public static string Money(decimal value) => value.ToString("N2", De) + " €";

    public static string Quantity(decimal value) => value.ToString("0.###", De);

    public static string OneLine(string? address) =>
        string.Join(" · ", Lines(address));

    public static IEnumerable<string> Lines(string? text) =>
        (text ?? string.Empty)
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0);
}
