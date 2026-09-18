# Implementation Plan: Squelette Applicatif Backend

**Branch**: `002-scaffold-dotnet` | **Date**: 2026-09-18 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-scaffold-dotnet/spec.md`

## Summary

Créer un squelette d'API ASP.NET Core (C#) en architecture en couches (Domain / Application /
Infrastructure / Api), démarrable en une commande (`docker compose up`), connecté à `kekebeautyDb`
existante, exposant un endpoint de santé applicative et un endpoint de vérification lecture/écriture
réelle sur la base — sans aucune règle métier fonctionnelle.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (LTS)

**Primary Dependencies**: ASP.NET Core 8 (minimal hosting), Npgsql (driver PostgreSQL), Dapper (accès
aux données léger, cohérent avec les migrations SQL brutes déjà en place — pas d'ORM à migrations
propres qui entrerait en conflit avec `scripts/db/migrate.sh`), AspNetCore.HealthChecks.NpgSql

**Storage**: PostgreSQL 16 `kekebeautyDb` (déjà en place, feature 001-infra-postgres)

**Testing**: xUnit pour les tests unitaires/d'intégration futurs (aucun test automatisé n'est exigé
par la spec de cette feature ; le projet de test est créé vide pour ne pas bloquer les features suivantes)

**Target Platform**: Conteneur Linux (image `mcr.microsoft.com/dotnet/aspnet:8.0`), exécuté via
Docker Desktop au côté du conteneur PostgreSQL déjà existant

**Project Type**: API web backend (single project côté serveur, pas de frontend dans cette feature)

**Performance Goals**: N/A à ce stade — hors périmètre (cf. spec, Assumptions)

**Constraints**: Aucune règle métier implémentée (FR-007) ; configuration de connexion externalisée
(FR-008, via variables d'environnement / `appsettings.json` non commité pour les secrets)

**Scale/Scope**: Un seul service API, 2 endpoints (santé applicative, santé base de données)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principe | Vérification | Statut |
|---|---|---|
| I. Spec-Driven Development | Suit constitution → specify → plan (en cours) → tasks → implement | PASS |
| II. MERISE (NON-NEGOTIABLE) | Aucune nouvelle entité créée ; les entités Domain reprendront exactement le MLD/MPD existants lors des futures features métier | PASS |
| III. PostgreSQL conteneurisé | Connexion à `kekebeautyDb` déjà conteneurisée ; aucune migration de schéma dupliquée ici (Dapper choisi précisément pour éviter un second système de migration) | PASS |
| IV. Gouvernance Scrum | Feature préalable non présente telle quelle dans le Product Backlog initial — à ajouter comme item technique du Sprint 1 (voir tasks.md, Polish) | PASS (avec action de suivi) |
| V. Sécurité des données sensibles | Aucune donnée sensible traitée (pas d'OTP/KYC/paiement dans cette feature) — N/A | PASS (N/A) |

Aucune violation constatée.

## Project Structure

### Documentation (this feature)

```text
specs/002-scaffold-dotnet/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── KekeBeauty.Domain/
│   └── Entities/                  # POCO reflétant le MLD (vides de comportement pour l'instant)
├── KekeBeauty.Application/
│   └── Health/
│       ├── IHealthDataCheck.cs    # Interface : vérifie lecture/écriture réelle
│       └── HealthDataCheckResult.cs
├── KekeBeauty.Infrastructure/
│   ├── DbConnectionFactory.cs     # Fabrique de connexions Npgsql à partir de la config
│   └── Health/
│       └── HealthDataCheck.cs     # Implémentation Dapper : SELECT 1 + INSERT/DELETE transactionnel
└── KekeBeauty.Api/
    ├── Program.cs                  # Minimal hosting, enregistrement des couches, health checks
    ├── appsettings.json            # Config non sensible (placeholders)
    ├── Dockerfile
    └── KekeBeauty.Api.csproj

tests/
└── KekeBeauty.Api.Tests/           # Projet de test vide (xUnit), prêt pour les futures features

db/migrations/
└── 0002_health_check_table.sql     # Table technique dédiée à la vérification d'écriture réelle

docker-compose.yml                   # Ajout du service "api" (déjà existant : postgres, pgadmin)
```

**Structure Decision**: Architecture en 4 projets .NET (Clean Architecture allégée) : `Domain` ne
dépend de rien, `Application` dépend de `Domain`, `Infrastructure` implémente les interfaces
d'`Application` et dépend de `Domain`, `Api` compose le tout au démarrage (Program.cs). Cette
séparation correspond directement à FR-005/FR-006 : une future feature métier (ex. Établissement)
ajoute une entité dans `Domain`, une interface + un cas d'usage dans `Application`, une implémentation
dans `Infrastructure`, et un contrôleur dans `Api` — sans toucher à l'organisation existante.

## Complexity Tracking

Aucune violation de la Constitution Check — section non applicable.
