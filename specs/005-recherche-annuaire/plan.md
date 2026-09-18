# Implementation Plan: Recherche et Consultation de l'Annuaire par le Client

**Branch**: `005-recherche-annuaire` | **Date**: 2026-09-18 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/005-recherche-annuaire/spec.md`

## Summary

Ajouter une recherche publique d'établissements (filtrage catégorie/commune, exclusion automatique
des non-validés et sans catégorie via un `INNER JOIN`), une fiche établissement publique (détail,
médias, prestations, lien d'itinéraire Google Maps), et un mécanisme minimal admin (protégé par la
clé déjà en place, feature 004) pour assigner catégorie/prestation en l'absence de gestion partenaire.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (cohérent avec les features précédentes)

**Primary Dependencies**: Dapper/Npgsql (déjà en place), aucune nouvelle dépendance

**Storage**: PostgreSQL `kekebeautyDb` — tables `etablissement`, `etablissement_categorie`,
`categorie`, `media`, `prestation`, `commune` déjà existantes, aucune nouvelle table

**Testing**: Tests manuels via `quickstart.md`

**Target Platform**: Conteneur `kekebeauty-api` existant

**Project Type**: Extension de l'API backend existante

**Performance Goals**: N/A (hors périmètre, cf. spec Assumptions — pas de pagination/tri à ce stade)

**Constraints**: FR-007 — la fiche d'un établissement non validé doit répondre exactement comme un
établissement inexistant (même code, même corps de réponse)

**Scale/Scope**: 2 endpoints publics (recherche, détail), 2 endpoints admin minimaux (assignation)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principe | Vérification | Statut |
|---|---|---|
| I. Spec-Driven Development | Suit constitution → specify → plan (en cours) → tasks → implement | PASS |
| II. MERISE (NON-NEGOTIABLE) | Lecture pure des entités déjà modélisées, aucune redéfinition | PASS |
| III. PostgreSQL conteneurisé | Aucune nouvelle table ; requêtes sur schéma déjà migré | PASS |
| IV. Gouvernance Scrum | Correspond à US-06 à US-09 du Product Backlog | PASS |
| V. Sécurité des données sensibles | FR-007 empêche toute fuite d'information sur les dossiers en attente/rejetés ; endpoints d'assignation réutilisent la protection admin déjà en place (pas de nouvelle surface non protégée) | PASS |

Aucune violation constatée.

## Project Structure

### Documentation (this feature)

```text
specs/005-recherche-annuaire/
├── plan.md
├── research.md
├── data-model.md
├── contracts/
├── quickstart.md
└── tasks.md
```

### Source Code (repository root)

```text
src/KekeBeauty.Application/
└── Directory/
    ├── EtablissementSummary.cs / EtablissementDetail.cs (classes, materialisees par Dapper)
    ├── IDirectoryRepository.cs         # recherche + detail (lecture)
    ├── ICategorieRepository.cs         # find-or-create par libelle (FR-008)
    ├── IPrestationRepository.cs        # ajout minimal (FR-008)
    ├── SearchEtablissementsUseCase.cs
    ├── GetEtablissementDetailUseCase.cs
    ├── AssignCategoryUseCase.cs
    └── AddPrestationUseCase.cs

src/KekeBeauty.Infrastructure/
└── Directory/
    ├── DirectoryRepository.cs      # Dapper, INNER JOIN etablissement_categorie (FR-002)
    ├── CategorieRepository.cs
    └── PrestationRepository.cs

src/KekeBeauty.Api/
└── Controllers/
    ├── EtablissementsController.cs   # GET /etablissements (recherche), GET /etablissements/{id} (fiche)
    └── AdminController.cs            # extension : POST .../categories, POST .../prestations (proteges)
```

**Structure Decision**: Nouveau domaine `Directory` (recherche/consultation publique), distinct
d'`Onboarding` (soumission/décision KYC) même s'il partage la protection admin pour FR-008 — cohérent
avec la convention posée en `002`, chaque domaine fonctionnel ayant son propre sous-dossier.

## Complexity Tracking

Aucune violation de la Constitution Check — section non applicable.
