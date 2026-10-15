# Implementation Plan: Lot 0 — Socle

**Spec**: [spec.md](spec.md) | **Constitution**: `.specify/memory/constitution.md`

## Technical Context
- Backend: .NET 10 (SDK 10.0.401), ASP.NET Core Minimal APIs, EF Core 10 + Npgsql + NetTopologySuite, Serilog, health checks, OpenAPI.
- Frontend: Angular (latest stable) standalone + signals + OnPush, PrimeNG with custom `KekeBeautyPreset` (derived from Aura), Playfair Display + Inter.
- Infra: Docker Compose — `postgis/postgis:16-3.4`, `minio/minio`, api, web (nginx).
- Tests: xUnit + FluentAssertions; Testcontainers PostGIS for integration.
- Design: `design-system/keke-beauty/MASTER.md` (Soft UI Evolution), tokens from PRD §6.2.

## Structure (PRD §5.3)
```
KekeBeauty.sln (root, opens in Visual Studio)
src/backend/{src/{Api,SharedKernel,Infrastructure,Modules/*}, tests/{Unit,Integration}}
src/frontend/keke-beauty-web/src/app/{core,shared,theme,features/public}
docker-compose.yml
```

## Decisions
- Un seul `KekeBeautyDbContext` dans Infrastructure, un schéma par module (config `IEntityTypeConfiguration` fournie par chaque module). Lot 0 : entités Geography + Catalog.categories seulement (YAGNI).
- Seed idempotent exécuté au démarrage en Development.
- Endpoints lot 0 : `/health/live`, `/health/ready`, `/api/v1/categories`, `/api/v1/geo/*`.
- Client TS OpenAPI : spec exposée sur `/openapi/v1.json` ; génération branchée plus tard (hors lot 0 exécution).
