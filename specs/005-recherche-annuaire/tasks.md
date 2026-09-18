---

description: "Task list template for feature implementation"
---

# Tasks: Recherche et Consultation de l'Annuaire par le Client

**Input**: Design documents from `/specs/005-recherche-annuaire/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Non explicitement demandés — `quickstart.md` sert de plan de validation manuelle.

**Organization**: Tâches groupées par User Story (US1, US2, US3 = P1 ; US4 = P2).

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [x] T001 Aucune nouvelle dépendance/configuration nécessaire (feature de lecture pure sur schéma
  existant) — phase de setup vide, passer directement à la Phase 2.

## Phase 2: Foundational (bloquant pour toutes les User Stories)

- [x] T002 [P] Créer `src/KekeBeauty.Application/Directory/Dtos.cs` : classes `EtablissementSummary`,
  `MediaDto`, `PrestationDto`, `EtablissementDetail` (voir `data-model.md`)
- [x] T003 [P] Créer `src/KekeBeauty.Application/Directory/IDirectoryRepository.cs` (recherche par
  catégorie/commune, détail par id — tous deux filtrés sur `statut_kyc = 'VALIDE'`)
- [x] T004 [P] Créer `src/KekeBeauty.Application/Directory/ICategorieRepository.cs`
  (`FindOrCreateByLibelleAsync`)
- [x] T005 [P] Créer `src/KekeBeauty.Application/Directory/IPrestationRepository.cs` (`AddAsync`)
- [x] T006 Créer `src/KekeBeauty.Infrastructure/Directory/DirectoryRepository.cs` (Dapper, `INNER JOIN
  etablissement_categorie` — voir `research.md` Décision 1)
- [x] T007 [P] Créer `src/KekeBeauty.Infrastructure/Directory/CategorieRepository.cs` (Dapper,
  `ON CONFLICT (libelle_categorie) DO NOTHING`)
- [x] T008 [P] Créer `src/KekeBeauty.Infrastructure/Directory/PrestationRepository.cs` (Dapper, `INSERT`)

## Phase 3: User Story 4 - Données minimales de test (Priority: P2, mais bloquante techniquement)

**Goal**: Pouvoir assigner catégorie/prestation à un établissement validé (FR-008), prérequis pour
tester réellement US1-US3 de bout en bout.

**Independent Test**: Scénario 1 de `quickstart.md`.

- [x] T009 [US4] Créer `src/KekeBeauty.Application/Directory/AssignCategoryUseCase.cs`
- [x] T010 [US4] Créer `src/KekeBeauty.Application/Directory/AddPrestationUseCase.cs`
- [x] T011 [US4] Étendre `src/KekeBeauty.Api/Controllers/AdminController.cs` : `POST
  /admin/applications/{id}/categories`, `POST /admin/applications/{id}/prestations` (même
  `[ServiceFilter(typeof(AdminApiKeyFilter))]` que les endpoints existants)
- [x] T012 [US4] Enregistrer les nouveaux services dans le DI (`Program.cs`)
- [x] T013 [US4] Exécuter le Scénario 1 de `quickstart.md` sur l'établissement validé de la feature 004

## Phase 4: User Story 1 - Recherche par catégorie et localisation (Priority: P1) 🎯 MVP

**Goal**: `GET /etablissements` retourne les établissements validés correspondant aux filtres.

**Independent Test**: Scénario 2 de `quickstart.md`.

- [x] T014 [US1] Créer `src/KekeBeauty.Application/Directory/SearchEtablissementsUseCase.cs`
- [x] T015 [US1] Créer `src/KekeBeauty.Api/Controllers/EtablissementsController.cs` : `GET
  /etablissements?categorie=&commune=` (public, mappé selon `contracts/directory-api.md`)
- [x] T016 [US1] Exécuter le Scénario 2 de `quickstart.md` (résultat non vide puis liste vide)

**Checkpoint**: US1 + US4 livrables — la recherche fonctionne de bout en bout avec des données réelles.

## Phase 5: User Story 2 - Exclusion des établissements non validés (Priority: P1)

**Goal**: Un établissement `EN_ATTENTE`/`REJETE` n'apparaît jamais en recherche.

**Independent Test**: Scénario 3 de `quickstart.md`.

- [x] T017 [US2] Exécuter le Scénario 3 de `quickstart.md` (soumission 004 + assignation catégorie
  sur un établissement `EN_ATTENTE`, puis recherche) et confirmer son absence des résultats

**Checkpoint**: US1 + US2 + US4 livrées.

## Phase 6: User Story 3 - Consultation de la fiche établissement (Priority: P1)

**Goal**: `GET /etablissements/{id}` retourne la fiche complète, ou `404` uniforme si non éligible.

**Independent Test**: Scénarios 4 et 5 de `quickstart.md`.

- [x] T018 [US3] Créer `src/KekeBeauty.Application/Directory/GetEtablissementDetailUseCase.cs`
  (génère `lienItineraire`, voir `research.md` Décision 5)
- [x] T019 [US3] Ajouter `GET /etablissements/{id}` à `EtablissementsController.cs` (404 uniforme,
  `research.md` Décision 2)
- [x] T020 [US3] Exécuter le Scénario 4 de `quickstart.md` (fiche complète)
- [x] T021 [US3] Exécuter le Scénario 5 de `quickstart.md` (404 identique non-validé/inexistant)

**Checkpoint**: US1 + US2 + US3 + US4 livrées — feature complète.

## Phase 7: Polish & Cross-Cutting Concerns

- [x] T022 [P] Mettre à jour `README.md` (section API backend) : endpoints de recherche/fiche,
  note sur le mécanisme FR-008 temporaire
- [x] T023 Mettre à jour `docs/scrum/product-backlog.md` (US-06 à US-09 → `005-recherche-annuaire`)
  et créer `docs/scrum/sprint-4.md`

## Dependencies & Execution Order

- Setup (Phase 1) : vide.
- Foundational (Phase 2, T002-T008) : bloque tout.
- User Story 4 (Phase 3) : dépend de la Phase 2 ; bloquante en pratique pour tester US1-US3 (données).
- User Story 1 (Phase 4) : dépend de la Phase 3 (données de test nécessaires). MVP.
- User Story 2 (Phase 5) : dépend de la Phase 4 (même mécanisme de recherche).
- User Story 3 (Phase 6) : dépend de la Phase 2 (indépendante de la recherche elle-même).
- Polish (Phase 7) : après toutes les User Stories.

## Parallel Example

```text
T002, T003, T004, T005 [P] peuvent être écrits en parallèle (interfaces, fichiers distincts)
T007, T008 [P] peuvent être écrits en parallèle une fois T004/T005 posées
```

## Implementation Strategy

**MVP first** : Phase 3 (US4, données) → Phase 4 (US1, recherche) avant Phases 5-6. La recherche sans
données de test ne peut pas être validée de bout en bout, d'où la priorité technique de US4 malgré sa
priorité produit P2.
