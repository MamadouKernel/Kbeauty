# Feature Specification: Lot 0 — Socle

**Feature Branch**: `001-lot0-socle` | **Created**: 2026-10-11 | **Status**: In progress
**Input**: PRD_Keke_Beauty.md §12 « Lot 0 : Socle »

## User Scenarios & Testing

### User Story 1 — Tout démarre avec une commande (Priority: P1)
En tant que développeur, je lance `docker compose up` et toute la plateforme locale démarre (PostGIS, MinIO, API, web).

**Independent Test**: `docker compose up -d` puis `GET /health/ready` renvoie 200 et la page d'accueil répond.

**Acceptance Scenarios**
1. **Given** un poste avec Docker, **When** `docker compose up`, **Then** postgres (PostGIS), minio, api et web sont healthy.
2. **Given** l'API démarrée, **When** elle se connecte à PostgreSQL, **Then** les migrations s'appliquent et les extensions `postgis`, `btree_gist`, `citext`, `unaccent`, `pg_trgm` existent.

### User Story 2 — Page d'accueil aux couleurs Keke Beauty (Priority: P1)
En tant que visiteur, je vois une page d'accueil élégante (violet/blanc, logo, Playfair Display + Inter) qui interroge `/health` de l'API et affiche l'état.

**Independent Test**: ouvrir `http://localhost:4200` : logo, hero, recherche factice, état de l'API « en ligne ».

**Acceptance Scenarios**
1. **Given** l'API disponible, **When** la page se charge, **Then** un indicateur « Service en ligne » apparaît.
2. **Given** l'API indisponible, **When** la page se charge, **Then** l'indicateur passe à « Service indisponible » sans erreur bloquante.
3. **Given** un écran de 375 px, **When** la page s'affiche, **Then** pas de défilement horizontal, cibles ≥ 44 px.

### User Story 3 — Référentiels amorcés (Priority: P2)
En tant que développeur, la base contient Côte d'Ivoire, régions, villes, communes d'Abidjan et les 7 catégories (PRD §8.4).

**Acceptance Scenarios**
1. **Given** une base vide, **When** l'API démarre en Development, **Then** `GET /api/v1/categories` renvoie 7 catégories et `GET /api/v1/geo/countries` renvoie la Côte d'Ivoire.

### User Story 4 — Qualité outillée (Priority: P2)
Build, tests et lint tournent en CI ; le contrat OpenAPI est exposé pour générer le client TypeScript.

## Requirements
- **FR-001**: Solution .NET modulaire (Api, SharedKernel, Infrastructure, 8 modules, tests).
- **FR-002**: EF Core 10 + Npgsql + NetTopologySuite, convention snake_case, un schéma par module, migrations.
- **FR-003**: Serilog JSON, health checks `/health/live` et `/health/ready`, OpenAPI.
- **FR-004**: Workspace Angular standalone + PrimeNG + `KekeBeautyPreset`, logo dans `public/brand/`.
- **FR-005**: `docker-compose.yml` (postgres-postgis, minio, api, web).
- **FR-006**: Seed des référentiels (Geography, Catalog categories).
- **FR-007**: CI (build, tests, lint).

## Success Criteria
- **SC-001**: `docker compose up` → tout healthy en < 3 min à froid.
- **SC-002**: Tests unitaires et d'intégration Testcontainers verts.
- **SC-003**: Bundle initial Angular < 300 Ko gzip.
