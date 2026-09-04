using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TreizeInvoice.Data;
using TreizeInvoice.Domain.Entities;
using TreizeInvoice.Domain.Enums;
using TreizeInvoice.Services.Backup;

namespace TreizeInvoice.Services.Compliance;

/// <summary>
/// Rédige la Verfahrensdokumentation à partir de l'état réel de l'installation.
/// Rien n'est inventé : chaque chiffre cité dans le PDF provient de la base.
/// </summary>
public class VerfahrensdokumentationService : IVerfahrensdokumentationService
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private readonly AppDbContext _db;
    private readonly BackupPaths _paths;

    static VerfahrensdokumentationService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public VerfahrensdokumentationService(AppDbContext db, BackupPaths paths)
    {
        _db = db;
        _paths = paths;
    }

    public async Task<VerfahrensdokumentationFacts> CollectAsync(CancellationToken cancellationToken = default)
    {
        var profile = await _db.BusinessProfiles.AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken) ?? new BusinessProfile();

        // Une seule lecture des factures : la volumétrie reste modeste (une
        // indépendante) et cela évite six allers-retours SQL.
        var invoices = await _db.Invoices.AsNoTracking()
            .Select(i => new
            {
                i.Status,
                i.InvoiceNumber,
                Year = i.InvoiceDate.Year,
                IsStorno = i.CancelsInvoiceId != null
            })
            .ToListAsync(cancellationToken);

        var ranges = invoices
            .Where(i => i.InvoiceNumber != null)
            .GroupBy(i => i.Year)
            .OrderBy(g => g.Key)
            .Select(g => new NumberRange(
                g.Key,
                g.Count(),
                g.Min(i => i.InvoiceNumber),
                g.Max(i => i.InvoiceNumber)))
            .ToList();

        var journalCount = await _db.JournalEntries.CountAsync(cancellationToken);
        var auditCount = await _db.AuditLogs.CountAsync(cancellationToken);

        DateTime? firstAudit = null;
        DateTime? lastAudit = null;
        if (auditCount > 0)
        {
            firstAudit = await _db.AuditLogs.MinAsync(a => a.TimestampUtc, cancellationToken);
            lastAudit = await _db.AuditLogs.MaxAsync(a => a.TimestampUtc, cancellationToken);
        }

        return new VerfahrensdokumentationFacts(
            CreatedAt: DateTime.Now,
            OwnerName: profile.FullName,
            OwnerAddress: profile.Address,
            Steuernummer: profile.Steuernummer,
            IsKleinunternehmer: profile.IsKleinunternehmer,
            NumberFormat: string.IsNullOrWhiteSpace(profile.NumberFormat)
                ? "{year}-{counter:0000}"
                : profile.NumberFormat,
            DatabasePath: _paths.DatabasePath,
            ArchivePath: _paths.ArchiveRoot,
            DraftCount: invoices.Count(i => i.Status == InvoiceStatus.Draft),
            IssuedCount: invoices.Count(i => i.Status == InvoiceStatus.Issued),
            PaidCount: invoices.Count(i => i.Status == InvoiceStatus.Paid),
            CancelledCount: invoices.Count(i => i.Status == InvoiceStatus.Cancelled),
            StornoCount: invoices.Count(i => i.IsStorno),
            Ranges: ranges,
            JournalEntryCount: journalCount,
            AuditEntryCount: auditCount,
            FirstAuditUtc: firstAudit,
            LastAuditUtc: lastAudit);
    }

    public async Task<GeneratedDocument> CreateAsync(CancellationToken cancellationToken = default)
    {
        var facts = await CollectAsync(cancellationToken);
        var pdf = Render(facts);
        var name = $"verfahrensdokumentation-{facts.CreatedAt:yyyyMMdd}.pdf";
        return new GeneratedDocument(name, pdf);
    }

    public static byte[] Render(VerfahrensdokumentationFacts f) =>
        Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(9.5f).FontColor("#1a1a1a").LineHeight(1.35f));

                page.Header().Element(c => Header(c, f));
                page.Content().PaddingVertical(14).Element(c => Body(c, f));
                page.Footer().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor("#777777"));
                    t.Span("Verfahrensdokumentation · ");
                    t.Span(f.OwnerName);
                    t.Span(" · Seite ");
                    t.CurrentPageNumber();
                    t.Span(" von ");
                    t.TotalPages();
                });
            });
        }).GeneratePdf();

    private static void Header(IContainer container, VerfahrensdokumentationFacts f)
    {
        container.BorderBottom(1).BorderColor("#cccccc").PaddingBottom(8).Column(col =>
        {
            col.Item().Text("Verfahrensdokumentation").FontSize(17).Bold();
            col.Item().Text("zur digitalen Rechnungsstellung und Belegablage nach GoBD")
                .FontSize(10).FontColor("#555555");
            col.Item().PaddingTop(6).Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(9).FontColor("#555555"));
                t.Span("Erstellt am ");
                t.Span(f.CreatedAt.ToString("dd.MM.yyyy 'um' HH:mm", De)).SemiBold();
                t.Span(" · Stand der Auswertung zum Erstellungszeitpunkt");
            });
        });
    }

    private static void Body(IContainer container, VerfahrensdokumentationFacts f)
    {
        container.Column(col =>
        {
            col.Spacing(13);

            Section(col, "1. Allgemeine Beschreibung", c =>
            {
                Table(c, new (string, string)[]
                {
                    ("Unternehmen", Fallback(f.OwnerName)),
                    ("Anschrift", Fallback(f.OwnerAddress.Replace("\r\n", ", ").Replace("\n", ", "))),
                    ("Steuernummer / USt-IdNr.", Fallback(f.Steuernummer)),
                    ("Besteuerungsform", f.IsKleinunternehmer
                        ? "Kleinunternehmer nach § 19 UStG — es wird keine Umsatzsteuer ausgewiesen."
                        : "Regelbesteuerung."),
                    ("Eingesetzte Software", "TreizeInvoice (Eigenbetrieb, Einzelplatz-Installation)")
                });

                c.Item().PaddingTop(6).Text(
                    "Diese Dokumentation beschreibt, wie Ausgangsrechnungen entstehen, wie sie " +
                    "nummeriert, festgeschrieben, archiviert und gesichert werden. Sie richtet sich " +
                    "an die Finanzverwaltung im Rahmen einer Außenprüfung sowie an sachverständige " +
                    "Dritte im Sinne der GoBD.");
            });

            Section(col, "2. Anwenderdokumentation — Ablauf der Rechnungsstellung", c =>
            {
                Steps(c,
                    "Entwurf: Kunde, Leistungspositionen, Leistungsdatum bzw. -zeitraum und " +
                        "Zahlungsziel werden erfasst. Der Entwurf trägt bewusst noch keine " +
                        "Rechnungsnummer und ist beliebig änder- und löschbar.",
                    "Ausstellen: In einer einzigen Datenbanktransaktion wird die nächste " +
                        "Rechnungsnummer des Jahres vergeben, das PDF erzeugt und unveränderbar " +
                        "abgelegt. Schlägt ein Schritt fehl, wird die gesamte Transaktion " +
                        "zurückgerollt und die Nummer nicht verbraucht — es entstehen keine Lücken.",
                    "Nach dem Ausstellen ist ein Bearbeiten oder Löschen technisch ausgeschlossen. " +
                        "Die Anwendung weist Änderungsversuche mit einer Fehlermeldung ab.",
                    "Korrektur: ausschließlich über eine Stornorechnung, die auf die " +
                        "ursprüngliche Rechnung verweist und deren Beträge mit umgekehrtem " +
                        "Vorzeichen ausweist. Anschließend kann eine neue, korrigierte Rechnung " +
                        "gestellt werden. Beide Belege bleiben dauerhaft erhalten.",
                    "Zahlungseingang: Die Rechnung wird als bezahlt markiert; dabei entsteht " +
                        "automatisch eine Einnahmebuchung im Journal mit Bezug zur Rechnungsnummer.");
            });

            Section(col, "3. Belegsicherung und Unveränderbarkeit", c =>
            {
                Table(c, new (string, string)[]
                {
                    ("Nummernkreis", $"Ein fortlaufender Kreis je Kalenderjahr, Format „{f.NumberFormat}“."),
                    ("Vergabe", "Atomar über einen Zähler je Jahr innerhalb der Ausstellungstransaktion. " +
                        "Parallele Vorgänge können denselben Zähler nicht doppelt beziehen."),
                    ("Archivierung", "Das beim Ausstellen erzeugte PDF wird schreibgeschützt abgelegt. " +
                        "Ein erneutes Schreiben unter demselben Namen wird vom Dateisystem abgelehnt."),
                    ("Auslieferung", "Downloads liefern immer die archivierte Datei, niemals eine " +
                        "Neuberechnung. Spätere Änderungen an Stamm- oder Kundendaten wirken sich " +
                        "nicht rückwirkend auf ausgestellte Rechnungen aus."),
                    ("Protokollierung", "Anlegen, Ausstellen, Stornieren und Zahlungseingang werden " +
                        "in einem ausschließlich anfügenden Prüfprotokoll festgehalten.")
                });
            });

            Section(col, "4. Technische Systemdokumentation", c =>
            {
                Table(c, new (string, string)[]
                {
                    ("Datenhaltung", "Relationale Datenbank (SQLite) mit Write-Ahead-Logging."),
                    ("Speicherort der Datenbank", f.DatabasePath),
                    ("Speicherort des PDF-Archivs", f.ArchivePath),
                    ("Ablagestruktur", "Ein Unterverzeichnis je Kalenderjahr, darin eine PDF-Datei je " +
                        "Rechnungsnummer."),
                    ("Zugriffsschutz", "Die Anwendung ist ausschließlich nach Anmeldung erreichbar. " +
                        "Kennwörter werden nicht im Klartext, sondern als PBKDF2-Ableitung gespeichert.")
                });
            });

            Section(col, "5. Nachweis der Nummernvergabe", c =>
            {
                if (f.Ranges.Count == 0)
                {
                    c.Item().Text("Zum Erstellungszeitpunkt wurde noch keine Rechnung ausgestellt.")
                        .Italic().FontColor("#555555");
                    return;
                }

                c.Item().Table(t =>
                {
                    t.ColumnsDefinition(d =>
                    {
                        d.ConstantColumn(70);
                        d.ConstantColumn(80);
                        d.RelativeColumn();
                        d.RelativeColumn();
                    });

                    t.Header(h =>
                    {
                        HeadCell(h, "Jahr");
                        HeadCell(h, "Anzahl");
                        HeadCell(h, "Erste Nummer");
                        HeadCell(h, "Letzte Nummer");
                    });

                    foreach (var r in f.Ranges)
                    {
                        BodyCell(t, r.Year.ToString(CultureInfo.InvariantCulture));
                        BodyCell(t, r.IssuedCount.ToString(CultureInfo.InvariantCulture));
                        BodyCell(t, r.First ?? "—");
                        BodyCell(t, r.Last ?? "—");
                    }
                });

                c.Item().PaddingTop(6).Text(
                    "Die Anzahl entspricht der Zahl der tatsächlich vergebenen Nummern des Jahres. " +
                    "Stornorechnungen erhalten eine eigene Nummer aus demselben Kreis und sind in " +
                    "der Anzahl enthalten.");
            });

            Section(col, "6. Bestand zum Erstellungszeitpunkt", c =>
            {
                Table(c, new (string, string)[]
                {
                    ("Entwürfe (ohne Nummer)", f.DraftCount.ToString(CultureInfo.InvariantCulture)),
                    ("Ausgestellt, offen", f.IssuedCount.ToString(CultureInfo.InvariantCulture)),
                    ("Bezahlt", f.PaidCount.ToString(CultureInfo.InvariantCulture)),
                    ("Storniert", f.CancelledCount.ToString(CultureInfo.InvariantCulture)),
                    ("davon Stornorechnungen", f.StornoCount.ToString(CultureInfo.InvariantCulture)),
                    ("Journalbuchungen", f.JournalEntryCount.ToString(CultureInfo.InvariantCulture)),
                    ("Einträge im Prüfprotokoll", f.AuditEntryCount.ToString(CultureInfo.InvariantCulture)),
                    ("Protokollzeitraum", f.FirstAuditUtc is null || f.LastAuditUtc is null
                        ? "—"
                        : $"{f.FirstAuditUtc.Value.ToLocalTime().ToString("dd.MM.yyyy", De)} bis " +
                          $"{f.LastAuditUtc.Value.ToLocalTime().ToString("dd.MM.yyyy", De)}")
                });
            });

            Section(col, "7. Betriebsdokumentation — Datensicherung", c =>
            {
                Steps(c,
                    "Über die Einstellungen kann jederzeit eine vollständige Sicherung als " +
                        "ZIP-Datei erzeugt werden. Sie enthält eine konsistente Kopie der " +
                        "Datenbank sowie sämtliche archivierten Rechnungs-PDFs.",
                    "Die Datenbankkopie wird mit einem konsistenten Momentaufnahme-Verfahren " +
                        "erstellt und ist auch bei laufendem Betrieb in sich schlüssig.",
                    "Die Sicherungen sind an einem vom Betriebssystem getrennten Ort " +
                        "aufzubewahren. Verantwortlich hierfür ist die Unternehmerin.",
                    "Das Einnahmen-Journal lässt sich als CSV-Datei exportieren und ist damit " +
                        "in gängige Auswertungs- und Steuerprogramme übernehmbar.");
            });

            Section(col, "8. Aufbewahrung", c =>
            {
                c.Item().Text(
                    "Rechnungen und die zugehörigen Aufzeichnungen werden über die gesetzliche " +
                    "Aufbewahrungsfrist unverändert, vollständig und maschinell auswertbar " +
                    "aufbewahrt. Die Belege bleiben während der gesamten Frist im " +
                    "Ursprungsformat lesbar; eine Umwandlung findet nicht statt.");
                c.Item().PaddingTop(6).Text(
                    "Diese Verfahrensdokumentation ist bei jeder wesentlichen Änderung des " +
                    "Verfahrens neu zu erzeugen. Frühere Fassungen sind zusammen mit den " +
                    "Buchführungsunterlagen aufzubewahren, damit für jeden Zeitraum die " +
                    "seinerzeit gültige Fassung nachweisbar bleibt.");
            });

            col.Item().PaddingTop(10).BorderTop(1).BorderColor("#cccccc").PaddingTop(8).Text(
                    "Hinweis: Dieses Dokument wurde automatisch aus dem tatsächlichen Zustand der " +
                    "Installation erzeugt und beschreibt das eingesetzte Verfahren. Es ersetzt " +
                    "keine steuerliche Beratung. Angaben zu Organisation und Aufbewahrungsort " +
                    "der Sicherungen sind von der Unternehmerin zu verantworten.")
                .FontSize(8).FontColor("#666666").Italic();
        });
    }

    private static void Section(ColumnDescriptor col, string title, Action<ColumnDescriptor> content)
    {
        col.Item().Column(c =>
        {
            c.Spacing(5);
            c.Item().Text(title).FontSize(11.5f).Bold().FontColor("#1F2E45");
            content(c);
        });
    }

    private static void Table(ColumnDescriptor col, IReadOnlyList<(string Label, string Value)> rows)
    {
        col.Item().Table(t =>
        {
            t.ColumnsDefinition(d =>
            {
                d.ConstantColumn(150);
                d.RelativeColumn();
            });

            foreach (var (label, value) in rows)
            {
                t.Cell().BorderBottom(0.5f).BorderColor("#e2e2e2").PaddingVertical(4).PaddingRight(8)
                    .Text(label).SemiBold();
                t.Cell().BorderBottom(0.5f).BorderColor("#e2e2e2").PaddingVertical(4)
                    .Text(value);
            }
        });
    }

    private static void Steps(ColumnDescriptor col, params string[] steps)
    {
        foreach (var (step, index) in steps.Select((s, i) => (s, i + 1)))
        {
            col.Item().Row(row =>
            {
                row.ConstantItem(18).Text($"{index}.").SemiBold().FontColor("#2946B8");
                row.RelativeItem().Text(step);
            });
        }
    }

    private static void HeadCell(TableCellDescriptor h, string text) =>
        h.Cell().BorderBottom(1).BorderColor("#1F2E45").PaddingVertical(4).PaddingRight(8)
            .Text(text).SemiBold().FontSize(9);

    private static void BodyCell(TableDescriptor t, string text) =>
        t.Cell().BorderBottom(0.5f).BorderColor("#e2e2e2").PaddingVertical(4).PaddingRight(8)
            .Text(text);

    private static string Fallback(string value) =>
        string.IsNullOrWhiteSpace(value) ? "— nicht hinterlegt —" : value;
}
