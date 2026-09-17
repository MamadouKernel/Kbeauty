# Implementation Plan: Infrastructure Base de Données Reproductible

**Branch**: `001-infra-postgres` | **Date**: 2026-09-17 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-infra-postgres/spec.md`

## Summary

Fournir un environnement PostgreSQL local démarrable en une commande (`docker compose up`), avec un
schéma initial conforme au MPD MERISE déjà validé, et un mécanisme de migrations SQL versionnées et
idempotentes permettant de reconstruire ou de faire évoluer ce schéma de façon reproductible et
traçable, sans jamais modifier une base existante à la main.

## Technical Context

**Language/Version**: SQL (PostgreSQL 16 dialect) ; scripts shell (bash) pour l'orchestration

**Primary Dependencies**: Docker Desktop / Docker Compose, image `postgres:16-alpine`, client `psql`

**Storage**: PostgreSQL 16 (conteneurisé), volume Docker nommé pour la persistance

**Testing**: Vérification par script (`scripts/db/migrate.sh --check`) rejouant les migrations sur un
conteneur jetable et comparant l'état du schéma obtenu à l'état attendu

**Target Platform**: Poste de développement (Windows/macOS/Linux avec Docker Desktop) ; portable vers
tout hôte Docker pour un environnement futur (test/production)

**Project Type**: Infrastructure de données (pas d'application applicative dans cette feature)

**Performance Goals**: N/A à ce stade (pas de charge en développement) — hors périmètre de cette feature

**Constraints**: Doit fonctionner hors-ligne après le premier téléchargement d'image ; ne doit jamais
nécessiter d'accès direct manuel à la base pour appliquer un changement de schéma

**Scale/Scope**: Un schéma unique, ~13 tables (cf. MPD), volumes de développement/test uniquement

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principe | Vérification | Statut |
|---|---|---|
| I. Spec-Driven Development | Cette feature suit le flux constitution → specify → plan (en cours) → tasks → implement | PASS |
| II. MERISE (NON-NEGOTIABLE) | Le schéma appliqué est directement dérivé du MPD validé (`docs/merise/05-mpd.sql`), sans redéfinition ad hoc | PASS |
| III. PostgreSQL conteneurisé | Docker Desktop / docker-compose, migrations versionnées, reconstruction depuis vide exigée par FR-005 | PASS |
| IV. Gouvernance Scrum | Correspond aux User Stories US-01/US-02 du Product Backlog | PASS |
| V. Sécurité des données sensibles | Aucune donnée sensible traitée dans cette feature (infra pure) — N/A | PASS (N/A) |

Aucune violation constatée. Pas d'entrée nécessaire dans Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/001-infra-postgres/
├── plan.md              # Ce fichier
├── research.md          # Phase 0
├── data-model.md         # Phase 1
├── quickstart.md         # Phase 1
└── tasks.md              # Phase 2 (généré par /speckit-tasks)
```

### Source Code (repository root)

```text
db/
├── migrations/
│   └── 0001_init_schema.sql   # Migration initiale = MPD validé (docs/merise/05-mpd.sql)
└── init/
    └── 000_bootstrap.sql      # Crée uniquement la table de suivi schema_migrations

scripts/
└── db/
    └── migrate.sh             # Applique les migrations en attente, de façon idempotente

docker-compose.yml              # Déjà existant : service postgres + pgadmin
```

**Structure Decision**: Le dossier `db/init/` (chargé une seule fois par Postgres, au premier démarrage
du volume) ne contient plus que le bootstrap de la table `schema_migrations` — pas le schéma métier.
Le schéma métier complet devient la migration `0001_init_schema.sql`, appliquée par `scripts/db/migrate.sh`.
Ce découpage permet de satisfaire à la fois US-01 (une commande suffit : `docker compose up -d && ./scripts/db/migrate.sh`)
et US-02 (toute évolution future est un nouveau fichier `NNNN_description.sql` dans `db/migrations/`,
rejouable depuis un environnement vide).

## Complexity Tracking

Aucune violation de la Constitution Check — section non applicable.
