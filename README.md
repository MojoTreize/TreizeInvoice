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
- `/app/parametres` — informations d'entreprise + changement de mot de passe

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
- [ ] Bloc 3 — émission + PDF
- [ ] Bloc 4 — paiement + storno
- [ ] Bloc 5 — journal + export EÜR
- [ ] Bloc 6 — tableau de bord + finitions
