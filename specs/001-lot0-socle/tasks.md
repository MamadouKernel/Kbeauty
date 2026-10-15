# Tasks: Lot 0 — Socle

## Phase 1: Setup
- [x] T001 Create backend solution + 8 modules + SharedKernel + Infrastructure + Api (`src/backend/`, root `KekeBeauty.sln`)
- [x] T002 [P] Create Angular workspace + PrimeNG (`src/frontend/keke-beauty-web/`, `.esproj` for Visual Studio)
- [x] T003 [P] `docker-compose.yml`, Dockerfiles, `.env.example`, `.gitignore`, `.gitattributes`

## Phase 2: Foundational
- [x] T004 SharedKernel: Result, base entity (domain events deferred: YAGNI until lot 4)
- [x] T005 Infrastructure: DbContext (snake_case, NetTopologySuite, extensions), initial migration
- [x] T006 Api: Serilog, health checks, OpenAPI + Scalar docs, ProblemDetails, CORS
- [x] T007 Theme: `KekeBeautyPreset` + global SCSS tokens + self-hosted fonts

## Phase 3: US1 — docker compose up (P1)
- [x] T008 Integration test: extensions exist + health ready (Testcontainers)
- [x] T009 Compose healthchecks + migration on startup (4 services healthy)

## Phase 4: US2 — Landing page (P1)
- [x] T010 Landing page (hero, search, categories, health badge) per MASTER.md
- [x] T011 Health service (signal) + graceful degradation

## Phase 5: US3 — Seed (P2)
- [x] T012 Integration tests for seed + endpoints
- [x] T013 Geography + categories entities, seed, endpoints

## Phase 6: US4 — Quality (P2)
- [x] T014 CI (`.github/workflows/ci.yml`): .NET build + `dotnet format --verify-no-changes` + tests; Angular ESLint (angular-eslint, a11y rules) + Prettier check + tests + build
- [x] T015 README quickstart
- [x] T016 OpenAPI → TypeScript client: `npm run api:types` generates `src/app/core/api/api.generated.ts` (used by `CatalogService`)
- [x] T017 Runtime settings via `/config.json` (written at container start from environment variables)

## Closing notes
- PrimeNG is pinned to 21.1.10 (MIT, free): PrimeNG 22 requires a PrimeUI license key, so the frontend runs on Angular 21.
- MinIO: official images are unavailable; compose uses the `bitnamilegacy/minio` mirror (PRD §4 deviation, to revisit before production).
- Cold-start timing (SC-001, < 3 min) was not measured with a wiped Docker cache.
