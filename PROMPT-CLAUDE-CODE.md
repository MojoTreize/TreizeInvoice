# Prompt Claude Code — TreizeInvoice MVP (C#)

Copie tout ce qui suit dans Claude Code, ouvert à la racine du projet `TreizeInvoice/`.

---

## CONTEXTE

Je suis développeuse (Angular / ASP.NET Core). Je lance un petit business de création
de sites web pour PME en Allemagne, en tant que **Kleinunternehmerin (§19 UStG)**.
TreizeInvoice est mon outil de facturation : MVP mono-utilisateur, que j'utiliserai
pour de vrai, conforme aux exigences légales allemandes (§14 UStG + principes GoBD).
Plus tard il pourra devenir un SaaS multi-utilisateurs, mais ce n'est PAS le sujet
aujourd'hui.

## STRUCTURE DU DÉPÔT — À CRÉER EN PREMIER, NE PAS DÉVIER

La landing page existe déjà (HTML statique terminé, ne pas la modifier).
Le dépôt sépare strictement frontend et backend :

```
TreizeInvoice/
├── frontend/                          # landing page statique — DÉJÀ FAITE, ne pas toucher
│   ├── index.html                     # (j'y déposerai treizeinvoice-landing.html)
│   └── assets/                        # logos SVG, favicons PNG
│
├── backend/
│   ├── TreizeInvoice.sln
│   ├── src/
│   │   ├── TreizeInvoice.Domain/      # entités, enums, règles métier, exceptions métier
│   │   ├── TreizeInvoice.Data/        # DbContext EF Core, configurations, migrations
│   │   ├── TreizeInvoice.Services/    # logique applicative : facturation, numérotation,
│   │   │                              #   PDF (QuestPDF), storno, journal, audit
│   │   └── TreizeInvoice.Web/         # Blazor Server : Pages, Components, wwwroot,
│   │                                  #   Program.cs, authentification
│   ├── tests/
│   │   └── TreizeInvoice.Tests/       # xUnit — logique métier uniquement
│   └── data/                          # invoicesaas → treizeinvoice.db (SQLite) + archive/
│                                      #   (dans .gitignore)
├── .gitignore                         # bin/, obj/, backend/data/, *.user
└── README.md
```

Règles de dépendance strictes (une flèche = « référence ») :
`Web → Services → Data → Domain`. Domain ne référence RIEN.
Les règles légales (verrouillage, numérotation, storno) vivent dans Domain/Services,
jamais dans les pages Blazor.

## STACK IMPOSÉE

- .NET 8, ASP.NET Core, **Blazor Server** (une seule app web, pas d'API séparée pour le MVP)
- **EF Core + SQLite** (fichier `backend/data/treizeinvoice.db`)
- **QuestPDF** pour la génération des PDF (licence Community)
- Authentification minimale : un seul utilisateur, login + mot de passe hashé,
  cookie auth — les routes doivent être **`/login`** et **`/register`**
  (la landing page pointe déjà vers ces deux URLs)
- UI de l'app en **français**, factures PDF générées en **allemand** (clients allemands)
- Pas de Docker, pas de microservices, pas de CQRS — simple et propre

## MODÈLE DE DONNÉES

**BusinessProfile** (mes données, 1 seule ligne, éditable dans Paramètres)
- Nom complet, adresse, email, téléphone
- Steuernummer, IBAN + BIC + nom de banque
- Bool `IsKleinunternehmer` (défaut true)
- Format de numérotation (défaut : `{année}-{compteur:0000}`, ex. 2026-0001)

**Client**
- Nom/raison sociale, personne de contact, adresse complète, email, téléphone, notes
- Soft delete (un client avec factures ne se supprime jamais physiquement)

**Invoice**
- Statuts : `Draft` → `Issued` → `Paid` | `Cancelled`
- Champs : client (FK), date de facture, période/date de prestation,
  lignes (InvoiceItem : description, quantité, prix unitaire, total),
  total, notes libres, échéance de paiement (défaut 14 jours)
- `InvoiceNumber` : **attribué uniquement au moment du passage à Issued**
  (jamais en Draft) — séquence continue sans trous, par année, générée de façon
  atomique (transaction) pour éviter les doublons
- `IssuedAtUtc`, `PaidAtUtc`, chemin du PDF archivé
- Lien optionnel `CancelledByInvoiceId` / `CancelsInvoiceId` pour les Storno

**AuditLog**
- Table append-only : date UTC, entité, id, action, détail JSON
- Chaque création/émission/annulation/paiement y écrit une ligne

**JournalEntry** (journal recettes/dépenses pour l'EÜR)
- Date, type (Recette/Dépense), montant, description, catégorie,
  lien optionnel vers une Invoice (les recettes de factures payées
  s'y créent automatiquement), pièce jointe optionnelle (chemin fichier)

## RÈGLES MÉTIER NON NÉGOCIABLES (conformité allemande)

1. **Immutabilité GoBD** : une facture `Issued` ne peut PLUS être modifiée ni
   supprimée — ni via l'UI, ni via le service. Le service lève une exception si
   on tente. Seules transitions permises : Issued → Paid, Issued → Cancelled.
2. **Correction = Stornorechnung** : annuler une facture émise génère une
   facture d'annulation (montants négatifs, son propre numéro dans la séquence,
   référence claire « Storno zu Rechnung Nr. X ») + son PDF. L'originale passe
   à Cancelled. On peut ensuite créer une nouvelle facture corrigée.
3. **Mentions §14 UStG sur le PDF** (en allemand) :
   - Nom + adresse complets de l'émetteur et du client
   - Steuernummer de l'émetteur
   - Rechnungsdatum, Rechnungsnummer
   - Leistungsbeschreibung + Leistungsdatum/-zeitraum
   - Montants par ligne + total
   - Si Kleinunternehmer : la phrase exacte
     **« Gemäß § 19 UStG wird keine Umsatzsteuer berechnet. »**
   - Coordonnées bancaires (IBAN/BIC) + délai de paiement
   - En-tête avec le logo TreizeInvoice (version monochrome, `frontend/assets/`)
4. **Archivage** : à l'émission, le PDF est généré et stocké dans
   `backend/data/archive/{année}/{numéro}.pdf`. Ce fichier n'est jamais régénéré
   ni écrasé (le bouton « Télécharger » ressert toujours le fichier archivé).
5. Les montants sont en `decimal`, jamais en `double`. Arrondi commercial
   à 2 décimales (MidpointRounding.AwayFromZero). Devise : EUR.

## PLAN DE DÉVELOPPEMENT PAR BLOCS

Travaille bloc par bloc. À la fin de chaque bloc : la solution compile,
les tests passent, et tu me montres quoi vérifier manuellement avant de continuer.

**Bloc 0 — Squelette**
Créer toute la structure de dépôt ci-dessus. Solution + 5 projets avec les bonnes
références, EF Core + SQLite + première migration, QuestPDF installé, layout Blazor
de base, login mono-utilisateur sur `/login` (identifiants seedés au premier
lancement, mot de passe changeable), `/register` affiche pour l'instant une page
« Beta — Registrierung folgt » (le SaaS multi-utilisateurs viendra plus tard),
page Paramètres avec BusinessProfile. `.gitignore` et README inclus.

**Bloc 1 — Clients**
CRUD clients complet (liste avec recherche, création, édition, soft delete).

**Bloc 2 — Factures en brouillon**
Création/édition de factures Draft : choix client, lignes dynamiques
(ajouter/supprimer), calculs en direct, aperçu des totaux. Duplication d'une
facture existante comme nouveau brouillon.

**Bloc 3 — Émission + PDF**
Transition Draft → Issued : attribution atomique du numéro, verrouillage,
génération du PDF allemand avec QuestPDF (design sobre : en-tête avec logo,
bloc client, tableau des lignes, total, phrase §19, coordonnées bancaires,
pied de page), archivage, AuditLog.
Tests unitaires obligatoires : séquence de numérotation (pas de trous, pas de
doublons, y compris en concurrence), verrouillage, arrondis.

**Bloc 4 — Paiement + Storno**
Marquer payée (crée la recette dans le journal automatiquement).
Workflow Storno complet selon la règle 2. Tests sur le Storno.

**Bloc 5 — Journal + export EÜR**
Page journal recettes/dépenses (saisie manuelle des dépenses), totaux par
année et par catégorie, **export CSV** (colonnes : date, type, catégorie,
description, montant, n° facture).

**Bloc 6 — Tableau de bord + finitions**
Dashboard : CA de l'année, factures impayées (retard en jours mis en évidence),
5 dernières factures. Bouton de sauvegarde manuelle (zip horodaté du .db +
dossier archive). Servir `frontend/` comme site statique à la racine `/` de
l'app Web (l'app elle-même vit sous `/app`, login sous `/login`) OU documenter
dans le README le déploiement séparé (landing sur Cloudflare Pages,
app sur sous-domaine) — propose-moi les deux options avant d'implémenter.

## HORS PÉRIMÈTRE (v2, ne pas construire maintenant)

Multi-utilisateurs/SaaS, XRechnung/ZUGFeRD, envoi d'emails, relances
automatiques, TVA (je suis Kleinunternehmerin), paiement en ligne, i18n de l'UI.
Mais structure le code pour que XRechnung et la TVA soient ajoutables sans
tout casser (calcul des totaux et rendu PDF derrière des interfaces).

## QUALITÉ ATTENDUE

- Code lisible et commenté là où la logique est légale/métier (numérotation,
  verrouillage, Storno) — je dois pouvoir expliquer chaque règle à un contrôleur
  fiscal dans 5 ans
- Validation des saisies côté serveur (montants > 0, client requis, etc.)
- Messages d'erreur en français, clairs
- Tests unitaires sur toute la logique métier des blocs 3 et 4

Commence par le Bloc 0 : crée d'abord la structure de dossiers et la solution,
montre-moi l'arborescence obtenue, puis attends ma validation avant d'écrire
le code des entités.
