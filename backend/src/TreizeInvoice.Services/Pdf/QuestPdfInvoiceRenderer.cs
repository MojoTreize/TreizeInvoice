using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TreizeInvoice.Domain.Entities;
using TreizeInvoice.Services.Invoicing;

namespace TreizeInvoice.Services.Pdf;

/// <summary>Emplacement des ressources graphiques du PDF (logo d'en-tête).</summary>
public record PdfAssets(string? LogoPath);

/// <summary>
/// Facture allemande conforme §14 UStG. Toutes les mentions obligatoires y figurent :
/// coordonnées complètes de l'émettrice et du client, Steuernummer, numéro et date de
/// facture, description et date de prestation, montants par ligne et total, mention
/// §19 UStG le cas échéant, coordonnées bancaires et délai de paiement.
/// </summary>
public class QuestPdfInvoiceRenderer : IInvoicePdfRenderer
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private readonly IInvoiceTotalsCalculator _totals;
    private readonly PdfAssets _assets;

    static QuestPdfInvoiceRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public QuestPdfInvoiceRenderer(IInvoiceTotalsCalculator totals, PdfAssets assets)
    {
        _totals = totals;
        _assets = assets;
    }

    public byte[] Render(Invoice invoice, BusinessProfile profile)
    {
        var totals = _totals.Calculate(invoice);
        var isStorno = invoice.CancelsInvoiceId is not null;
        var dueDate = invoice.InvoiceDate.AddDays(invoice.PaymentTermDays);

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(10).FontColor("#1a1a1a"));

                page.Header().Element(c => ComposeHeader(c, profile));
                page.Content().Element(c => ComposeContent(c, invoice, profile, totals, isStorno, dueDate));
                page.Footer().Element(c => ComposeFooter(c, profile));
            });
        }).GeneratePdf();
    }

    private void ComposeHeader(IContainer container, BusinessProfile profile)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text(profile.FullName).FontSize(14).SemiBold();
            });

            if (!string.IsNullOrWhiteSpace(_assets.LogoPath) && File.Exists(_assets.LogoPath))
            {
                row.ConstantItem(90).AlignRight().Height(38)
                    .Svg(File.ReadAllText(_assets.LogoPath));
            }
        });
    }

    private static void ComposeContent(
        IContainer container,
        Invoice invoice,
        BusinessProfile profile,
        InvoiceTotals totals,
        bool isStorno,
        DateOnly dueDate)
    {
        container.PaddingTop(20).Column(col =>
        {
            col.Spacing(14);

            // Ligne expéditeur au-dessus de l'adresse du destinataire (usage postal allemand).
            col.Item().Text($"{profile.FullName} · {OneLine(profile.Address)}")
                .FontSize(7).FontColor(Colors.Grey.Darken1);

            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(invoice.Client.Name).SemiBold();
                    if (!string.IsNullOrWhiteSpace(invoice.Client.ContactPerson))
                        c.Item().Text(invoice.Client.ContactPerson);
                    foreach (var line in Lines(invoice.Client.Address))
                        c.Item().Text(line);
                });

                row.ConstantItem(200).Column(c =>
                {
                    c.Spacing(2);
                    Field(c, "Rechnungsnummer", invoice.InvoiceNumber ?? "—");
                    Field(c, "Rechnungsdatum", invoice.InvoiceDate.ToString("dd.MM.yyyy", De));
                    if (invoice.ServiceDate is { } sd)
                        Field(c, "Leistungsdatum", sd.ToString("dd.MM.yyyy", De));
                    if (!string.IsNullOrWhiteSpace(invoice.ServicePeriod))
                        Field(c, "Leistungszeitraum", invoice.ServicePeriod);
                    if (!string.IsNullOrWhiteSpace(profile.Steuernummer))
                        Field(c, "Steuernummer", profile.Steuernummer);
                });
            });

            col.Item().PaddingTop(10)
                .Text(isStorno
                    ? $"Stornorechnung Nr. {invoice.InvoiceNumber}"
                    : $"Rechnung Nr. {invoice.InvoiceNumber}")
                .FontSize(16).Bold();

            if (isStorno && invoice.CancelsInvoice is not null)
            {
                col.Item().Text($"Storno zu Rechnung Nr. {invoice.CancelsInvoice.InvoiceNumber} "
                                + $"vom {invoice.CancelsInvoice.InvoiceDate.ToString("dd.MM.yyyy", De)}")
                    .SemiBold();
            }

            col.Item().Element(c => ComposeItemsTable(c, invoice, totals));

            if (profile.IsKleinunternehmer)
            {
                // Mention obligatoire pour les Kleinunternehmer (§19 UStG) : formulation exacte.
                col.Item().PaddingTop(4)
                    .Text("Gemäß § 19 UStG wird keine Umsatzsteuer berechnet.");
            }

            col.Item().Text($"Zahlbar ohne Abzug bis zum {dueDate.ToString("dd.MM.yyyy", De)} "
                            + $"({invoice.PaymentTermDays} Tage).");

            col.Item().Column(c =>
            {
                c.Item().Text("Bankverbindung").SemiBold();
                c.Item().Text($"{profile.BankName}");
                c.Item().Text($"IBAN: {profile.Iban}");
                c.Item().Text($"BIC: {profile.Bic}");
            });

            if (!string.IsNullOrWhiteSpace(invoice.Notes))
                col.Item().PaddingTop(6).Text(invoice.Notes);
        });
    }

    private static void ComposeItemsTable(IContainer container, Invoice invoice, InvoiceTotals totals)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(28);
                c.RelativeColumn();
                c.ConstantColumn(60);
                c.ConstantColumn(80);
                c.ConstantColumn(80);
            });

            table.Header(h =>
            {
                HeaderCell(h, "Pos.");
                HeaderCell(h, "Beschreibung");
                HeaderCell(h, "Menge", true);
                HeaderCell(h, "Einzelpreis", true);
                HeaderCell(h, "Betrag", true);
            });

            var position = 1;
            foreach (var item in invoice.Items)
            {
                BodyCell(table, position.ToString());
                BodyCell(table, item.Description);
                BodyCell(table, item.Quantity.ToString("0.###", De), true);
                BodyCell(table, Money(item.UnitPrice), true);
                BodyCell(table, Money(item.LineTotal), true);
                position++;
            }

            table.Cell().ColumnSpan(4).BorderTop(1).PaddingTop(6).AlignRight()
                .Text("Gesamtbetrag").SemiBold();
            table.Cell().BorderTop(1).PaddingTop(6).AlignRight()
                .Text(Money(totals.Gross)).SemiBold();
        });
    }

    private static void ComposeFooter(IContainer container, BusinessProfile profile)
    {
        container.BorderTop(1).BorderColor(Colors.Grey.Lighten1).PaddingTop(6)
            .Text(text =>
            {
                text.DefaultTextStyle(t => t.FontSize(7).FontColor(Colors.Grey.Darken1));
                text.Span($"{profile.FullName} · {OneLine(profile.Address)}");
                if (!string.IsNullOrWhiteSpace(profile.Email))
                    text.Span($" · {profile.Email}");
                if (!string.IsNullOrWhiteSpace(profile.Phone))
                    text.Span($" · {profile.Phone}");
                if (!string.IsNullOrWhiteSpace(profile.Steuernummer))
                    text.Span($" · Steuernummer: {profile.Steuernummer}");
            });
    }

    private static void Field(ColumnDescriptor col, string label, string value) =>
        col.Item().Row(r =>
        {
            r.ConstantItem(95).Text(label).FontColor(Colors.Grey.Darken1);
            r.RelativeItem().Text(value).SemiBold();
        });

    private static void HeaderCell(TableCellDescriptor header, string text, bool right = false)
    {
        var cell = header.Cell().BorderBottom(1).PaddingVertical(4);
        (right ? cell.AlignRight() : cell).Text(text).SemiBold();
    }

    private static void BodyCell(TableDescriptor table, string text, bool right = false)
    {
        var cell = table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4);
        (right ? cell.AlignRight() : cell).Text(text);
    }

    private static string Money(decimal value) => value.ToString("N2", De) + " €";

    private static IEnumerable<string> Lines(string? text) =>
        (text ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string OneLine(string? text) => string.Join(", ", Lines(text));
}
