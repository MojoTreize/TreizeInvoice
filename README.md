# TreizeInvoice

Rechnungsstellung für Kleinunternehmer in Deutschland — konform nach § 14 UStG und GoBD.

## Aufbau
- `frontend/` — statische Landingpage
- `backend/` — .NET-8-Projektmappe (Blazor Server)
- `PROMPT-CLAUDE-CODE.md` — die vollständige Projektbeschreibung

## Backend — Architektur
Strenge Abhängigkeitsrichtung: `Web → Services → Data → Domain` (Domain referenziert nichts).

```
backend/
├── TreizeInvoice.sln
├── src/
│   ├── TreizeInvoice.Domain/    # Entitäten, Enums, fachliche Ausnahmen
│   ├── TreizeInvoice.Data/      # EF-Core-DbContext, Konfiguration, Migrationen
│   ├── TreizeInvoice.Services/  # Anwendungslogik (Rechnungen, Journal, PDF, Auth)
│   └── TreizeInvoice.Web/       # Blazor Server (Seiten, Cookie-Authentifizierung)
├── tests/TreizeInvoice.Tests/   # xUnit
└── data/                        # SQLite + archivierte PDFs (in .gitignore)
```

Technik: .NET 8, Blazor Server, EF Core mit SQLite, QuestPDF, Cookie-Authentifizierung.
Oberfläche und Rechnungen sind vollständig deutsch; die Kultur wird in `Program.cs` fest
auf `de-DE` gesetzt, damit Zahlen- und Datumsformate nicht von der Servereinstellung
abhängen.

> Hinweis zur Umgebung: Die Projekte zielen auf `net8.0`. Das Web-Projekt nutzt
> `RollForward=Major`, um auf der vorhandenen .NET-9/10-Laufzeit zu starten (keine
> Administratorrechte zur Installation der 8er-Laufzeit).

## Start
```powershell
cd backend
dotnet build TreizeInvoice.sln
dotnet run --project src/TreizeInvoice.Web --urls http://localhost:5287
```

Die SQLite-Datenbank (`backend/data/treizeinvoice.db`) wird beim ersten Start angelegt und
migriert, zusammen mit einem Benutzerkonto und einem leeren Unternehmensprofil.

### Erster Start — Zugangsdaten
Es sind keine Zugangsdaten im Quelltext hinterlegt. Beim allerersten Start legt die
Anwendung den Benutzer `admin` mit einem **zufälligen Passwort an, das genau einmal in der
Konsole erscheint**:

```
warn: Zugang angelegt — Benutzername: admin / Passwort: xxxxxxxx.
      Bitte notieren und in den Einstellungen ändern.
```

Eigene Zugangsdaten legen Sie vor dem ersten Start fest — über User Secrets, niemals in
einer versionierten Datei:

```powershell
cd backend/src/TreizeInvoice.Web
dotnet user-secrets init
dotnet user-secrets set "Seed:Username" "meinbenutzer"
dotnet user-secrets set "Seed:Password" "meinPasswort"
```

Das Passwort lässt sich danach jederzeit unter **Einstellungen** ändern.

Nach fünf Fehlversuchen wird die Anmeldung fünf Minuten gesperrt (`LoginThrottle`). Bei
einer Einzelplatzanwendung ist der Benutzername bekannt — allein das Passwort schützt die
gesamte Buchhaltung.

### Routen
| Route | Zweck |
| --- | --- |
| `/` | Landingpage (statisch, aus `frontend/`) |
| `/login` | Anmeldung |
| `/register` | Hinweis auf den Beta-Zugang |
| `/impressum`, `/datenschutz` | Rechtstexte der Landingpage |
| `/app` | Übersicht (geschützt) |
| `/app/kunden`, `/app/kunden/neu`, `/app/kunden/{id}` | Kundenverwaltung |
| `/app/rechnungen`, `/app/rechnungen/neu`, `/app/rechnungen/{id}` | Rechnungen |
| `/app/rechnungen/{id}/pdf` | archiviertes PDF einer gestellten Rechnung |
| `/app/journal` | Einnahmen und Ausgaben |
| `/app/journal/export?year=2026` | CSV-Export des Jahres |
| `/app/einstellungen` | Unternehmensangaben, Passwort, Nachweise |
| `/app/sicherung` | ZIP mit Datenbank und allen archivierten PDFs |
| `/app/verfahrensdokumentation` | Verfahrensdokumentation als PDF |

## Umgesetzte Regeln
- **Nummernkreis**: Die Nummer wird ausschließlich beim Ausstellen vergeben, fortlaufend je
  Kalenderjahr, innerhalb einer Transaktion hochgezählt (`InvoiceNumberSequences`). Schlägt
  das Ausstellen fehl, wird der Zähler nicht verbraucht — es entstehen keine Lücken.
- **Unveränderbarkeit (GoBD)**: Eine gestellte Rechnung lässt sich weder ändern noch
  löschen. Der Schutz liegt im Service (`InvoiceLockedException`), nicht nur in der
  Oberfläche.
- **Archivierung**: Das PDF wird nach `backend/data/archive/{Jahr}/{Nummer}.pdf`
  geschrieben, mit `FileMode.CreateNew` — ein vorhandenes Dokument wird nie überschrieben.
  Der Download liefert immer genau diese Datei, niemals eine Neuberechnung.
- **Pflichtangaben nach § 14 UStG** auf jedem PDF, dazu der Satz „Gemäß § 19 UStG wird
  keine Umsatzsteuer berechnet." bei Kleinunternehmerregelung.
- **Korrektur ausschließlich per Storno**: Die Stornorechnung erhält negative Beträge, eine
  eigene Nummer, ein eigenes archiviertes PDF und den Verweis „Storno zu Rechnung Nr. X";
  die Ursprungsrechnung wechselt nach `Cancelled`.
- **Zahlung**: `Issued → Paid` erzeugt automatisch die Einnahme im Journal (Grundlage der
  EÜR). Eine so entstandene Buchung lässt sich nicht von Hand löschen.
- **Beträge** als `decimal`, kaufmännisch gerundet (`MidpointRounding.AwayFromZero`).

## Lebenszyklus einer Rechnung
Unter `Neue Rechnung` wird der Zielzustand bereits bei der Erfassung gewählt: als Entwurf
ablegen, ausstellen, oder ausstellen und sofort als bezahlt buchen. Der Entwurf ist keine
bürokratische Zwischenstufe, sondern die Folge des Nummernkreises: Eine Nummer entsteht
erst beim Ausstellen, sonst hinterließe jede abgebrochene Erfassung eine Lücke.

Eine gestellte Rechnung wird nicht bearbeitet (GoBD). `Korrigieren` führt die einzig
zulässige Korrektur in einem Schritt aus: Stornorechnung, anschließend ein neuer Entwurf
mit denselben Positionen, den Sie berichtigen und neu ausstellen. Beide Ursprungsbelege
bleiben erhalten.

Storniert werden kann auch eine **bereits bezahlte** Rechnung — die Kundin hat gezahlt, der
Fehler fällt auf, Sie erstatten. Die Einnahme im Journal wird nicht gelöscht (GoBD),
sondern durch eine Gegenbuchung am Storno neutralisiert; andernfalls zählte die EÜR
weiterhin einen erstatteten Zahlungseingang.

Bestätigungen erscheinen in einem zentrierten Dialog (`ConfirmDialog`). Am Seitenende
gerendert lagen sie unterhalb des sichtbaren Bereichs — ein Klick auf „Ausstellen" wirkte
folgenlos.

## Vorschau während der Erfassung
`Neue Rechnung` zeigt rechts das Dokument so, wie es archiviert wird, aktualisiert bei
jedem Tastendruck: Empfänger, Pflichtangaben nach § 14, § 19-Satz, berechnete Fälligkeit,
Bankverbindung. Fehlende Angaben sind dort vor dem Ausstellen sichtbar („Steuernummer
fehlt", „ohne Beschreibung") statt erst bei der Ablehnung.

PDF (`QuestPdfInvoiceRenderer`) und Vorschau (`InvoicePreview.razor`) lesen dieselben
Konstanten aus `InvoiceDocumentText`. Eine Vorschau, die eine im Enddokument fehlende
Angabe verspräche, wäre bei einem Werkzeug, das Konformität verkauft, schlimmer als gar
keine Vorschau; gemeinsame Zeichenketten machen ein Auseinanderlaufen der rechtlich
relevanten Formulierungen unmöglich (`InvoiceDocumentTextTests`).

## Prüfung der Unternehmensangaben
`ProfileValidation` prüft die Form, nicht nur das Vorhandensein: Eine Anschrift ohne
Postleitzahl und Ort genügt § 14 Abs. 4 Nr. 1 UStG nicht, und eine abgeschnittene IBAN
macht die Rechnung unbezahlbar. Die IBAN wird über die landesspezifische Länge und die
Modulo-97-Prüfung nach ISO 13616 kontrolliert. Fehlendes erscheint in den Einstellungen
und in der Vorschau und verhindert das Ausstellen.

## Rechnungsliste: Suche und Sortierung
Filterung, Sortierung und Seitenaufteilung laufen vollständig in SQL
(`InvoiceQuery` → `InvoiceService.SearchAsync`). Die Zahl der Rechnungen wächst unbegrenzt;
sie alle zu laden, um im Arbeitsspeicher zu filtern, trüge nicht. Gefiltert wird nach Text
(Nummer oder Kunde), Status, Zeitraum und Betragsspanne; sortiert nach allen fünf Spalten.

Die Sortierung nach Betrag stützt sich auf `Invoice.TotalCents`, eine vom Service
gepflegte Kopie der Summe. Zwei Gründe: `Total` wird aus den Positionen berechnet und
existiert nicht in der Datenbank, und SQLite legt `decimal` als TEXT ab — ein `ORDER BY`
sortierte dort alphabetisch („90" nach „1000"). Rechtlich maßgebliche Dokumente und alle
angezeigten Beträge werden weiterhin aus den Positionen berechnet; `TotalCents` dient
ausschließlich dem Sortieren und Filtern.

## CSV-Export (EÜR)
`journal-{Jahr}.csv`: Trennzeichen `;`, Dezimalkomma, Datum `TT.MM.JJJJ`, UTF-8 mit BOM —
öffnet sich direkt in einer deutschen Excel-Installation. Die Beträge sind
vorzeichenbehaftet (Ausgaben negativ), eine einfache Summe ergibt daher das Jahresergebnis.
Textfelder werden maskiert und Formeln entschärft (CSV-Injection).

## Datensicherung
Die Schaltfläche in den Einstellungen lädt ein ZIP mit Zeitstempel herunter, das die
Datenbank und sämtliche archivierten PDFs enthält. Die Momentaufnahme der Datenbank
entsteht mit `VACUUM INTO`: Im WAL-Modus ließe das bloße Kopieren der `.db`-Datei alle noch
im Journal stehenden Schreibvorgänge außen vor.

Bewahren Sie diese Sicherungen **außerhalb des Rechners** auf — die Aufbewahrungspflicht
beträgt zehn Jahre (§ 147 AO).

## Verfahrensdokumentation
Die GoBD (Rz. 151 ff.) verlangen eine schriftliche Beschreibung des Verfahrens: wie Belege
entstehen, nummeriert, festgeschrieben, archiviert und gesichert werden. Ihr Fehlen gehört
zu den häufigsten Beanstandungen in der Betriebsprüfung, und kein verbreitetes Werkzeug
erzeugt sie automatisch.

`Einstellungen → Nachweise für das Finanzamt` erstellt dieses Dokument als deutsches PDF
(`GET /app/verfahrensdokumentation`). Es ist kein Mustertext: `CollectAsync` liest den
tatsächlichen Zustand der Installation — Profil, Format des Nummernkreises, Speicherorte
von Datenbank und Archiv, die je Jahr wirklich vergebenen Nummernbereiche, die Verteilung
der Status, den Umfang des Journals und den Zeitraum des Prüfprotokolls.

Die Erhebung der Fakten ist vom Rendern getrennt (`VerfahrensdokumentationFacts`), damit
sie prüfbar ist, ohne ein PDF zu öffnen (siehe `VerfahrensdokumentationTests`).

Das Dokument ist bei jeder wesentlichen Änderung des Verfahrens neu zu erzeugen; frühere
Fassungen gehören zu den Buchführungsunterlagen.

## Betrieb

### Variante 0 — privat, ohne Veröffentlichung
Für eine einzelne Nutzerin am eigenen Rechner muss die Anwendung nicht online sein:
`dotnet run`, dann `http://localhost:5287`. Kein Server, keine Kosten, keine
Angriffsfläche, und die Daten verlassen den Rechner nicht.

Was dabei zu bedenken ist:
- Die Sicherung liegt **vollständig** in Ihrer Verantwortung (§ 147 AO: zehn Jahre). Das
  ZIP aus den Einstellungen gehört auf einen Datenträger außerhalb des Rechners.
- Die Anwendung ist nur an diesem Rechner erreichbar, nicht vom Telefon.
- Eine verlorene Festplatte bedeutet eine verlorene Buchhaltung.

**Netlify eignet sich nicht für die Anwendung.** Netlify liefert statische Dateien und
kurzlaufende Serverless-Funktionen. TreizeInvoice ist ein dauerhaft laufender .NET-Prozess
mit einer offenen WebSocket-Verbindung je Sitzung (Blazor Server) und einer SQLite-Datei
auf einem beständigen Datenträger. Keine der drei Voraussetzungen ist erfüllt. Für die
**Landingpage** dagegen ist Netlify (oder Cloudflare Pages) bestens geeignet, sie besteht
aus reinem HTML.

### Variante 1 — Landingpage öffentlich, Anwendung auf einem Server
Beide werden unabhängig voneinander veröffentlicht und kommunizieren nicht miteinander:
Die Landingpage ist statisches HTML und **verweist** lediglich auf die Anwendung — kein
API-Aufruf, folglich keine Fragen zu CORS oder geteilten Sitzungen.

```
treizeinvoice.de              Landingpage (statisch)   Cloudflare Pages
app.treizeinvoice.de          Anwendung (Blazor)       VPS (Hetzner, netcup …)
```

Vorteil: Die Landingpage bleibt erreichbar, auch wenn die Anwendung für Wartungsarbeiten
steht.

#### Landingpage — Cloudflare Pages
- GitHub-Repository verbinden, **Build command**: keine, **Output directory**: `frontend`
- Eigene Domain: `treizeinvoice.de`
- [`frontend/_redirects`](frontend/_redirects) leitet `/login` und `/register` auf die
  Subdomain der Anwendung um. So bleiben die Verweise in `index.html` relativ.
  **Domain dort anpassen.**

#### Anwendung — Linux-Server
Veröffentlichen und auf den Server kopieren:

```powershell
cd backend
dotnet publish src/TreizeInvoice.Web -c Release -o publish
```

Auf dem Server, `/etc/systemd/system/treizeinvoice.service`:

```ini
[Unit]
Description=TreizeInvoice
After=network.target

[Service]
WorkingDirectory=/var/www/treizeinvoice
ExecStart=/usr/bin/dotnet /var/www/treizeinvoice/TreizeInvoice.Web.dll
Restart=always
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://127.0.0.1:5000
Environment=Storage__DataPath=/var/lib/treizeinvoice
Environment=Storage__LogoPath=/var/www/treizeinvoice/assets/treizeinvoice-mark-mono.svg

[Install]
WantedBy=multi-user.target
```

`Caddyfile` (automatisches HTTPS, WebSocket für Blazor Server inbegriffen):

```
app.treizeinvoice.de {
    reverse_proxy 127.0.0.1:5000
}
```

#### Worauf zu achten ist
- **`Storage__DataPath`** außerhalb des Anwendungsverzeichnisses, sonst überschreibt eine
  neue Veröffentlichung Datenbank und archivierte PDFs.
- **Data-Protection-Schlüssel** liegen unter `{DataPath}/keys` und überdauern damit jedes
  Update; andernfalls würde jede Aktualisierung die Anmeldung ungültig machen.
- **Erster Start**: Das erzeugte Passwort steht in `journalctl -u treizeinvoice`.
  Alternativ über `Seed__Password` vorgeben.
- **Blazor Server** benötigt eine dauerhafte WebSocket-Verbindung, also einen ständig
  laufenden Server — kein statisches Hosting, kein Serverless.
- **Sicherungen**: `{DataPath}` enthält die gesamte Buchhaltung und gehört außerhalb des
  Servers gesichert.

## Datenbank — Migrationen
```powershell
cd backend
dotnet ef migrations add <Name> -p src/TreizeInvoice.Data -s src/TreizeInvoice.Data -o Migrations
```

## Tests
```powershell
cd backend
dotnet test tests/TreizeInvoice.Tests
```

## Stand
- [x] Grundgerüst, Einzelbenutzer-Authentifizierung, Einstellungen, erste Migration
- [x] Kunden: Liste mit Suche, Anlegen, Bearbeiten, Soft Delete
- [x] Rechnungsentwürfe: dynamische Positionen, laufende Summen, Duplizieren
- [x] Ausstellen samt deutschem PDF: atomare Nummernvergabe, Festschreibung, Archivierung
- [x] Zahlung (Einnahme im Journal) und Storno, auch für bezahlte Rechnungen
- [x] Journal für Einnahmen und Ausgaben, CSV-Export für die EÜR
- [x] Übersicht mit Kennzahlen und Datensicherung
- [x] Verfahrensdokumentation aus den tatsächlichen Daten
- [x] Suche, Filter, Sortierung und Seitenaufteilung der Rechnungsliste
- [x] Vorschau des Dokuments während der Erfassung
