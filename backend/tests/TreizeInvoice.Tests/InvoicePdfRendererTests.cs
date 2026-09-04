using TreizeInvoice.Domain.Entities;
using TreizeInvoice.Services.Invoicing;
using TreizeInvoice.Services.Pdf;

namespace TreizeInvoice.Tests;

public class InvoicePdfRendererTests
{
    private static BusinessProfile Profile() => new()
    {
        FullName = "Mimi Sagno",
        Address = "Beispielweg 3\n60311 Frankfurt",
        Email = "kontakt@example.de",
        Steuernummer = "013/456/78901",
        Iban = "DE02120300000000202051",
        Bic = "BYLADEM1001",
        BankName = "Beispielbank",
        IsKleinunternehmer = true
    };

    private static Invoice Invoice() => new()
    {
        InvoiceNumber = "2026-0001",
        InvoiceDate = new DateOnly(2026, 3, 1),
        ServiceDate = new DateOnly(2026, 2, 28),
        PaymentTermDays = 14,
        Client = new Client { Name = "Muster GmbH", Address = "Musterstr. 1\n10115 Berlin" },
        Items = { new InvoiceItem { Description = "Webseite — Gestaltung & Umsetzung", Quantity = 1, UnitPrice = 1200m } }
    };

    private static byte[] Render(string? logoPath) =>
        new QuestPdfInvoiceRenderer(new KleinunternehmerTotalsCalculator(), new PdfAssets(logoPath))
            .Render(Invoice(), Profile());

    [Fact]
    public void Render_ProduitUnPdfValide()
    {
        var pdf = Render(null);

        Assert.True(pdf.Length > 1000);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void Render_AvecLeLogoMonochromeReel()
    {
        var logo = FindRepoFile(Path.Combine("frontend", "assets", "treizeinvoice-mark-mono.svg"));
        Assert.NotNull(logo);

        var pdf = Render(logo);

        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.True(pdf.Length > Render(null).Length, "Le logo doit apparaître dans le document.");
    }

    private static string? FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
