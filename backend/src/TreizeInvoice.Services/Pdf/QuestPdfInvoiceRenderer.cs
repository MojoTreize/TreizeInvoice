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
            col.Item().Text(InvoiceDocumentText.SenderLine(profile))
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
                    Field(c, InvoiceDocumentText.InvoiceNumberLabel, invoice.InvoiceNumber ?? "—");
                    Field(c, InvoiceDocumentText.InvoiceDateLabel, InvoiceDocumentText.Date(invoice.InvoiceDate));
                    if (invoice.ServiceDate is { } sd)
                        Field(c, InvoiceDocumentText.ServiceDateLabel, InvoiceDocumentText.Date(sd));
                    if (!string.IsNullOrWhiteSpace(invoice.ServicePeriod))
                        Field(c, InvoiceDocumentText.ServicePeriodLabel, invoice.ServicePeriod);
                    if (!string.IsNullOrWhiteSpace(profile.Steuernummer))
                        Field(c, InvoiceDocumentText.TaxNumberLabel, profile.Steuernummer);
                });
            });

            col.Item().PaddingTop(10)
                .Text(InvoiceDocumentText.Title(isStorno, invoice.InvoiceNumber))
                .FontSize(16).Bold();

            if (isStorno && invoice.CancelsInvoice is not null)
            {
                col.Item().Text(InvoiceDocumentText.StornoReference(
                        invoice.CancelsInvoice.InvoiceNumber, invoice.CancelsInvoice.InvoiceDate))
                    .SemiBold();
            }

            col.Item().Element(c => ComposeItemsTable(c, invoice, totals));

            if (profile.IsKleinunternehmer)
            {
                col.Item().PaddingTop(4).Text(InvoiceDocumentText.KleinunternehmerNotice);
            }

            col.Item().Text(InvoiceDocumentText.PaymentTerms(dueDate, invoice.PaymentTermDays));

            col.Item().Column(c =>
            {
                c.Item().Text(InvoiceDocumentText.BankDetailsLabel).SemiBold();
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
                HeaderCell(h, InvoiceDocumentText.PositionHeader);
                HeaderCell(h, InvoiceDocumentText.DescriptionHeader);
                HeaderCell(h, InvoiceDocumentText.QuantityHeader, true);
                HeaderCell(h, InvoiceDocumentText.UnitPriceHeader, true);
                HeaderCell(h, InvoiceDocumentText.AmountHeader, true);
            });

            var position = 1;
            foreach (var item in invoice.Items)
            {
                BodyCell(table, position.ToString());
                BodyCell(table, item.Description);
                BodyCell(table, InvoiceDocumentText.Quantity(item.Quantity), true);
                BodyCell(table, Money(item.UnitPrice), true);
                BodyCell(table, Money(item.LineTotal), true);
                position++;
            }

            table.Cell().ColumnSpan(4).BorderTop(1).PaddingTop(6).AlignRight()
                .Text(InvoiceDocumentText.TotalLabel).SemiBold();
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
                text.Span(InvoiceDocumentText.SenderLine(profile));
                if (!string.IsNullOrWhiteSpace(profile.Email))
                    text.Span($" · {profile.Email}");
                if (!string.IsNullOrWhiteSpace(profile.Phone))
                    text.Span($" · {profile.Phone}");
                if (!string.IsNullOrWhiteSpace(profile.Steuernummer))
                    text.Span($" · {InvoiceDocumentText.TaxNumberLabel}: {profile.Steuernummer}");
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

    private static string Money(decimal value) => InvoiceDocumentText.Money(value);

    private static IEnumerable<string> Lines(string? text) => InvoiceDocumentText.Lines(text);
}
