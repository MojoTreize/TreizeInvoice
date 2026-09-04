# TreizeInvoice

Outil de facturation conforme (§14 UStG + GoBD) pour Kleinunternehmer en Allemagne.

## Structure
- `frontend/` — landing page statique (terminée, ne pas modifier)
- `backend/`  — solution .NET 8 / Blazor Server
- `PROMPT-CLAUDE-CODE.md` — le prompt complet du projet

## Backend — architecture
Dépendances strictes : `Web → Services → Data → Domain` (Domain ne référence rien).

```
backend/
├── TreizeInvoice.sln
├── src/
│   ├── TreizeInvoice.Domain/    # entités, enums, exceptions métier
│   ├── TreizeInvoice.Data/      # DbContext EF Core, configurations, migrations
│   ├── TreizeInvoice.Services/  # logique applicative (auth, profil, seed…)
│   └── TreizeInvoice.Web/       # Blazor Server (pages, auth cookie)
├── tests/TreizeInvoice.Tests/   # xUnit
└── data/                        # SQLite + PDF archivés (dans .gitignore)
```

Stack : .NET 8, Blazor Server, EF Core + SQLite, QuestPDF, authentification par cookie.
UI en français, factures PDF en allemand.

> Remarque environnement : les projets ciblent `net8.0` (stack imposé). Le projet
> Web utilise `RollForward=Major` pour s'exécuter sur le runtime .NET 9/10 présent
> (pas de droits admin pour installer le runtime 8).

## Démarrage (backend)
```powershell
cd backend
dotnet build TreizeInvoice.sln
dotnet run --project src/TreizeInvoice.Web --urls http://localhost:5287
```
La base SQLite (`backend/data/treizeinvoice.db`) est créée et migrée automatiquement
au premier lancement, avec un utilisateur par défaut et un profil d'entreprise vide.

### Premier lancement — compte initial
Aucun identifiant n'est codé en dur. Au tout premier démarrage (base vide), l'application
crée l'utilisateur `admin` avec un **mot de passe aléatoire affiché une seule fois dans la
console** :

```
warn: Compte initial créé — identifiant : admin / mot de passe : xxxxxxxx.
      Notez-le puis changez-le dans Paramètres.
```

Pour choisir vous-même les identifiants, définissez-les avant le premier lancement
(par exemple via les secrets utilisateur, jamais dans un fichier versionné) :

```powershell
cd backend/src/TreizeInvoice.Web
dotnet user-secrets init
dotnet user-secrets set "Seed:Username" "monidentifiant"
dotnet user-secrets set "Seed:Password" "monMotDePasse"
```

Le mot de passe se change ensuite à tout moment depuis **Paramètres**.

### Routes principales
- `/login` — connexion
- `/register` — inscription (page « Beta » pour l'instant)
- `/app` — tableau de bord (protégé)
- `/app/clients` — clients
- `/app/factures` — factures
- `/app/factures/{id}/pdf` — PDF archivé d'une facture émise
- `/app/journal` — journal recettes/dépenses
- `/app/journal/export?year=2026` — export CSV de l'année
- `/app/sauvegarde` — ZIP horodaté (base + PDF archivés)
- `/app/parametres` — informations d'entreprise + changement de mot de passe

## Conformité (règles implémentées)
- **Numérotation** : attribuée uniquement à l'émission, séquence continue par année,
  incrémentée dans une transaction (table `InvoiceNumberSequences`). Si l'émission
  échoue, le compteur n'est pas consommé → aucun trou.
- **Immutabilité GoBD** : une facture `Issued` ne peut plus être modifiée ni supprimée
  (`InvoiceLockedException` côté service, pas seulement dans l'UI).
- **Archivage** : le PDF est écrit dans `backend/data/archive/{année}/{numéro}.pdf`
  en `FileMode.CreateNew` (jamais écrasé). Le téléchargement ressert toujours ce fichier.
- **Mentions §14 UStG** sur le PDF, et « Gemäß § 19 UStG wird keine Umsatzsteuer
  berechnet. » si Kleinunternehmer.
- **Correction = Storno** : une facture émise ne se modifie pas. L'annulation crée une
  Stornorechnung (montants négatifs, numéro propre, PDF archivé, référence
  « Storno zu Rechnung Nr. X ») et passe l'originale en `Cancelled`.
- **Paiement** : `Issued → Paid` crée automatiquement la recette au journal (EÜR).
  Une écriture générée par une facture ne se supprime pas à la main.
- **Montants** en `decimal`, arrondi commercial `MidpointRounding.AwayFromZero`.

## Liste des factures : recherche et tri
Tout est appliqué en SQL puis paginé (`InvoiceQuery` → `InvoiceService.SearchAsync`) :
le nombre de factures grandit indéfiniment, les charger toutes pour filtrer en mémoire
ne tiendrait pas. Filtres : texte (numéro ou client), statut, période, fourchette de
montant. Tri sur les cinq colonnes.

Le tri par montant s'appuie sur `Invoice.TotalCents`, copie persistée du total tenue à
jour par le service. Deux raisons : `Total` est calculé depuis les lignes et n'existe
pas en base, et SQLite stocke les `decimal` en TEXT — un `ORDER BY` y serait
alphabétique (« 90 » après « 1000 »). Les documents légaux et les montants affichés
restent calculés depuis les lignes ; `TotalCents` ne sert qu'à trier et filtrer.

## Cycle de vie d'une facture
`Neue Rechnung` propose de choisir l'état visé dès la saisie : rester en brouillon,
émettre, ou émettre et encaisser aussitôt. Le brouillon n'est pas une étape
bureaucratique mais la conséquence de la numérotation : un numéro n'est attribué qu'à
l'émission, sinon une saisie abandonnée laisserait un trou dans la séquence.

Une facture émise ne se modifie pas (GoBD). `Korrigieren` enchaîne la seule correction
licite : Stornorechnung, puis nouveau brouillon reprenant les lignes, à rectifier et
réémettre. Les deux pièces d'origine restent en base.

## Aperçu du document pendant la saisie
`Neue Rechnung` affiche à droite le document tel qu'il sera archivé, mis à jour à la
frappe : destinataire, mentions §14, ligne §19, échéance calculée, coordonnées bancaires.
Les manques y sont visibles avant l'émission (« Steuernummer fehlt », « ohne
Beschreibung ») plutôt qu'au moment du refus.

Le PDF (`QuestPdfInvoiceRenderer`) et l'aperçu (`InvoicePreview.razor`) lisent les mêmes
constantes, `InvoiceDocumentText`. Un aperçu qui montrerait une mention absente du
document final serait pire que pas d'aperçu du tout sur un outil qui vend la conformité ;
partager les chaînes rend la dérive impossible sur ce qui a une portée juridique
(`InvoiceDocumentTextTests`).

## Export CSV (EÜR)
`journal-{année}.csv` : séparateur `;`, virgule décimale, dates `TT.MM.JJJJ`, UTF-8 avec BOM
— s'ouvre directement dans Excel en allemand. Les montants sont signés (dépenses négatives),
donc une simple somme donne le résultat de l'exercice.
Les champs texte sont échappés et les formules neutralisées (injection CSV).

## Sauvegarde
Le bouton « Sauvegarder maintenant » du tableau de bord télécharge un ZIP horodaté
contenant la base et tous les PDF archivés. L'instantané de la base est pris avec
`VACUUM INTO` : en mode WAL, copier le fichier `.db` seul laisserait de côté les
écritures encore en journal.
Conservez ces archives **hors de la machine** (les obligations GoBD portent sur 10 ans).

## Verfahrensdokumentation
Les GoBD (Rz. 151 ss.) exigent une description écrite du procédé : comment les pièces
naissent, sont numérotées, verrouillées, archivées et sauvegardées. Son absence est un
des reproches les plus fréquents en contrôle fiscal, et aucun outil grand public ne la
génère automatiquement.

`Einstellungen → Nachweise für das Finanzamt` produit ce document en PDF allemand
(`GET /app/verfahrensdokumentation`). Ce n'est pas un texte type : `CollectAsync` lit
l'état réel de l'installation — profil, format de numérotation, chemins de la base et
de l'archive, plages de numéros réellement attribuées par année, ventilation des
statuts, volumétrie du journal et période couverte par le journal d'audit.

La collecte des faits est séparée du rendu (`VerfahrensdokumentationFacts`) afin d'être
testable sans ouvrir un PDF (voir `VerfahrensdokumentationTests`).

Le document est à régénérer à chaque changement notable du procédé ; les versions
antérieures doivent être conservées avec les pièces comptables.

## Déploiement (landing et application séparées)

La landing et l'application sont déployées indépendamment. Elles ne communiquent pas :
la landing est du HTML statique qui **pointe** simplement vers l'application (aucun appel
d'API, donc aucune question de CORS ni de session partagée).

```
treizeinvoice.de              landing statique      Cloudflare Pages
app.treizeinvoice.de          application Blazor    VPS (Hetzner, netcup…)
```

Avantage : la vitrine reste en ligne même si l'application est arrêtée pour maintenance.

### 1. Landing — Cloudflare Pages
- Connecter le dépôt GitHub, **build command** : aucune, **output directory** : `frontend`
- Domaine personnalisé : `treizeinvoice.de`
- [`frontend/_redirects`](frontend/_redirects) renvoie `/login` et `/register` vers
  le sous-domaine de l'application : les liens de la landing restent relatifs et
  `index.html` n'a pas à être modifié. **Adaptez-y votre domaine.**

### 2. Application — VPS Linux

Publier puis copier sur le serveur :

```powershell
cd backend
dotnet publish src/TreizeInvoice.Web -c Release -o publish
```

Sur le serveur, `/etc/systemd/system/treizeinvoice.service` :

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

`Caddyfile` (HTTPS automatique, WebSocket pris en charge pour Blazor Server) :

```
app.treizeinvoice.de {
    reverse_proxy 127.0.0.1:5000
}
```

### Points d'attention
- **`Storage__DataPath`** : hors du dossier de l'application, sinon un redéploiement
  écraserait la base et les PDF archivés.
- **Clés de protection** : conservées dans `{DataPath}/keys`, elles survivent aux
  redéploiements (sinon chaque mise à jour déconnecte l'utilisateur).
- **Premier lancement** : le mot de passe généré apparaît dans `journalctl -u treizeinvoice`.
  Vous pouvez aussi le fixer via `Seed__Password`.
- **Blazor Server** exige une connexion WebSocket permanente : un serveur toujours
  actif est nécessaire (pas d'hébergement statique ni de serverless).
- **Sauvegardes** : `{DataPath}` contient toute la comptabilité. À sauvegarder hors serveur.

## Base de données — migrations
```powershell
cd backend
dotnet ef migrations add <Nom> -p src/TreizeInvoice.Data -s src/TreizeInvoice.Data -o Migrations
```

## Tests
```powershell
cd backend
dotnet test tests/TreizeInvoice.Tests
```

## Avancement
- [x] Bloc 0 — squelette, auth mono-utilisateur, paramètres, migration initiale
- [x] Bloc 1 — clients (liste + recherche, création, édition, soft delete)
- [x] Bloc 2 — factures brouillon (lignes dynamiques, totaux live, duplication)
- [x] Bloc 3 — émission + PDF allemand (numérotation atomique, verrouillage, archivage)
- [x] Bloc 4 — paiement (recette au journal) + Storno
- [x] Bloc 5 — journal recettes/dépenses + export CSV (EÜR)
- [x] Bloc 6 — tableau de bord + sauvegarde manuelle
  (hébergement de la landing page : à arbitrer)
- [x] Verfahrensdokumentation GoBD générée à partir des données réelles
