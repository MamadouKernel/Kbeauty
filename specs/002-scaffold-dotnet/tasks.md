---

description: "Task list template for feature implementation"
---

# Tasks: Squelette Applicatif Backend

**Input**: Design documents from `/specs/002-scaffold-dotnet/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: Non explicitement demandés dans la spec — projet de test créé vide (T004) pour ne pas
bloquer les futures features ; `quickstart.md` sert de plan de validation manuelle.

**Organization**: Tâches groupées par User Story (US1, US2 = P1 ; US3 = P2).

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [ ] T001 Créer la solution `.sln` et les 4 projets .NET 8 à la racine : `dotnet new sln -n KekeBeauty`,
  `dotnet new classlib -o src/KekeBeauty.Domain`, `dotnet new classlib -o src/KekeBeauty.Application`,
  `dotnet new classlib -o src/KekeBeauty.Infrastructure`, `dotnet new webapi -o src/KekeBeauty.Api`
  (minimal API, sans Swagger par défaut), puis `dotnet sln add` pour chaque projet
- [ ] T002 [P] Créer le projet de test vide `dotnet new xunit -o tests/KekeBeauty.Api.Tests`, l'ajouter
  à la solution
- [ ] T003 Configurer les références de projet : `Application` → `Domain` ; `Infrastructure` →
  `Application` + `Domain` ; `Api` → `Infrastructure` + `Application` + `Domain`
- [ ] T004 [P] Ajouter les paquets NuGet : `Npgsql`, `Dapper` dans `KekeBeauty.Infrastructure` ;
  `AspNetCore.HealthChecks.NpgSql` dans `KekeBeauty.Api`

## Phase 2: Foundational (bloquant pour toutes les User Stories)

- [ ] T005 Créer `db/migrations/0002_health_check_table.sql` : table technique `health_check`
  (`id UUID PK DEFAULT gen_random_uuid()`, `checked_at TIMESTAMPTZ NOT NULL DEFAULT now()`) — voir
  `data-model.md`
- [ ] T006 Appliquer la migration via `./scripts/db/migrate.sh` et vérifier la présence de la table
- [ ] T007 [P] Créer les entités vides dans `src/KekeBeauty.Domain/Entities/` : une classe C# par table
  du MLD (`Utilisateur.cs`, `Etablissement.cs`, `Categorie.cs`, `Pays.cs`, `Region.cs`, `Ville.cs`,
  `Commune.cs`, `Media.cs`, `Prestation.cs`, `Rdv.cs`, `Abonnement.cs`, `Transaction.cs`) — propriétés
  reflétant `docs/merise/04-mld.md`, sans logique
- [ ] T008 Créer `src/KekeBeauty.Infrastructure/DbConnectionFactory.cs` : fabrique de connexions
  `NpgsqlConnection` lisant la chaîne de connexion depuis la configuration (FR-008)
- [ ] T009 Configurer `src/KekeBeauty.Api/appsettings.json` (placeholders) + lecture de la chaîne de
  connexion depuis les variables d'environnement (`ConnectionStrings__Default`), pas de secret commité

## Phase 3: User Story 1 - Démarrage local de l'API backend (Priority: P1) 🎯 MVP

**Goal**: `docker compose up` démarre l'API, qui répond sur un point de vérification de santé.

**Independent Test**: Scénario 1 de `quickstart.md`.

- [ ] T010 [US1] Créer `src/KekeBeauty.Api/Program.cs` : hosting minimal ASP.NET Core, enregistrement
  des services (DI), mapping de l'endpoint `/health` (health check applicatif de base, sans dépendance
  base de données)
- [ ] T011 [US1] Créer `src/KekeBeauty.Api/Dockerfile` (build multi-stage `mcr.microsoft.com/dotnet/sdk:8.0`
  → `mcr.microsoft.com/dotnet/aspnet:8.0`)
- [ ] T012 [US1] Ajouter le service `api` dans `docker-compose.yml` : build depuis `src/KekeBeauty.Api`,
  `depends_on: postgres (condition: service_healthy)`, variable `API_PORT` configurable (comme
  `POSTGRES_PORT` déjà en place)
- [ ] T013 [US1] Exécuter le Scénario 1 de `quickstart.md` et confirmer la réponse `Healthy` sur `/health`
- [ ] T014 [US1] Exécuter le Scénario 4 de `quickstart.md` (`dotnet run` local hors Docker)

**Checkpoint**: US1 livrable de façon autonome.

## Phase 4: User Story 2 - Vérification de la connexion à la base de données (Priority: P1)

**Goal**: `/health/db` confirme une lecture ET une écriture réelles, échoue explicitement si la base
est indisponible.

**Independent Test**: Scénarios 2 et 3 de `quickstart.md`.

- [ ] T015 [US2] Créer `src/KekeBeauty.Application/Health/IHealthDataCheck.cs` (interface) et
  `HealthDataCheckResult.cs` (résultat : succès/échec + message)
- [ ] T016 [US2] Créer `src/KekeBeauty.Infrastructure/Health/HealthDataCheck.cs` : implémentation
  Dapper — `SELECT 1` (lecture), puis transaction `INSERT INTO health_check ... ; ROLLBACK` (écriture,
  jamais commitée) — voir `research.md` Décision 3
- [ ] T017 [US2] Enregistrer `HealthDataCheck` dans le conteneur DI (`Program.cs`) et mapper l'endpoint
  `/health/db` qui l'invoque et retourne 200 si succès, 503 avec message explicite si échec (FR-004)
- [ ] T018 [US2] Configurer le health check `AspNetCore.HealthChecks.NpgSql` en complément (visibilité
  standard ASP.NET Core), sans faire échouer le démarrage du processus si la base n'est pas encore
  prête (Edge Case — voir `research.md` Décision 5)
- [ ] T019 [US2] Exécuter le Scénario 2 de `quickstart.md` et confirmer qu'aucune ligne ne persiste
  dans `health_check` après l'appel
- [ ] T020 [US2] Exécuter le Scénario 3 de `quickstart.md` (base arrêtée) et confirmer la réponse 503
  explicite

**Checkpoint**: US1 + US2 livrées — le socle applicatif est démarrable et vérifiable de bout en bout.

## Phase 5: User Story 3 - Structure prête à accueillir les futurs modules (Priority: P2)

**Goal**: La séparation Domain/Application/Infrastructure/Api est documentée et vérifiable par revue.

**Independent Test**: Revue de structure (voir Acceptance Scenarios de la User Story 3 dans `spec.md`).

- [ ] T021 [P] [US3] Documenter dans `README.md` la convention d'ajout d'une future capacité métier :
  entité dans `Domain`, interface + cas d'usage dans `Application`, implémentation dans
  `Infrastructure`, contrôleur dans `Api`
- [ ] T022 [US3] Revue manuelle : décrire, pour l'entité `Etablissement` (déjà modélisée en MERISE),
  où prendrait place chaque partie d'une future feature d'annuaire — documenter le résultat en
  commentaire dans `specs/002-scaffold-dotnet/quickstart.md` ou en note de fin de tâche

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T023 [P] Mettre à jour `README.md` (section "API backend") avec les commandes de démarrage
  (`docker compose up -d --build`, endpoints `/health` et `/health/db`)
- [ ] T024 Ajouter au Product Backlog (`docs/scrum/product-backlog.md`) un item technique "Scaffold
  applicatif .NET" sous l'Epic 1 (Fondations techniques), référençant `002-scaffold-dotnet` — pas
  présent explicitement dans le backlog initial (constaté lors du Constitution Check du plan)
- [ ] T025 Créer `docs/scrum/sprint-1.md` et y consigner cette feature comme premier item du Sprint 1

## Dependencies & Execution Order

- Setup (Phase 1, T001-T004) : bloque tout — la solution .NET doit exister avant toute autre tâche.
- Foundational (Phase 2, T005-T009) : bloque les User Stories — migration, entités et configuration
  de connexion nécessaires avant d'écrire les endpoints.
- User Story 1 (Phase 3) : dépend de la Phase 2. Livrable seule (MVP).
- User Story 2 (Phase 4) : dépend de la Phase 3 (l'API doit démarrer avant de tester `/health/db`).
- User Story 3 (Phase 5) : dépend des Phases 3-4 (revue de structure sur du code existant).
- Polish (Phase 6) : après toutes les User Stories.

## Parallel Example

```text
T002 [P] et T004 [P] peuvent être faits en parallèle de T001/T003 une fois la solution créée
T007 [P] (entités Domain) peut être fait en parallèle de T008/T009 (fichiers distincts)
T021 [P] [US3] peut être fait en parallèle de T023 (fichiers distincts)
```

## Implementation Strategy

**MVP first** : Phase 3 (US1) avant Phase 4 (US2). US1 seule prouve que l'API démarre ; US2 sécurise
la fiabilité de l'accès aux données avant que les futures features métier ne s'appuient dessus.
US3 (Phase 5) est une revue documentaire, non bloquante pour démarrer le développement fonctionnel.
