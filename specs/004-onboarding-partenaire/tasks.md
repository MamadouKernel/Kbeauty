---

description: "Task list template for feature implementation"
---

# Tasks: Inscription et Validation KYC des Établissements Partenaires

**Input**: Design documents from `/specs/004-onboarding-partenaire/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Non explicitement demandés — `quickstart.md` sert de plan de validation manuelle.

**Organization**: Tâches groupées par User Story (US1, US2 = P1 ; US3 = P2).

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [x] T001 Créer `db/migrations/0004_geo_seed_minimal.sql` : seed minimal `pays`/`region`/`ville`/`commune`
  (voir `data-model.md`)
- [x] T002 Appliquer la migration via `./scripts/db/migrate.sh` et récupérer l'`id_commune` généré
- [x] T003 [P] Ajouter dans `.env.example`/`.env` : `ADMIN_API_KEY` (placeholder) ; dans
  `src/KekeBeauty.Api/appsettings.json` : `Admin:ApiKey` (vide)

## Phase 2: Foundational (bloquant pour toutes les User Stories)

- [x] T004 [P] Créer `src/KekeBeauty.Application/Onboarding/Dtos.cs` (résultats de soumission, résumé
  et détail de dossier)
- [x] T005 [P] Créer `src/KekeBeauty.Application/Onboarding/IEtablissementRepository.cs` (créer,
  lister par statut, obtenir par id, mettre à jour le statut)
- [x] T006 [P] Créer `src/KekeBeauty.Application/Onboarding/IFileStorage.cs` (sauvegarder un flux,
  ouvrir un flux en lecture, par chemin relatif)
- [x] T007 [P] Créer `src/KekeBeauty.Application/Onboarding/IPartnerNotifier.cs` (notifier un rejet)
- [x] T008 Créer `src/KekeBeauty.Infrastructure/Onboarding/EtablissementRepository.cs` (Dapper,
  utilise l'`id_commune` du seed T002)
- [x] T009 Créer `src/KekeBeauty.Infrastructure/Onboarding/LocalFileStorage.cs` (écrit sous
  `/app/storage/kyc/{id}/`, voir `research.md` Décision 1)
- [x] T010 Créer `src/KekeBeauty.Infrastructure/Onboarding/ZavuWhatsAppPartnerNotifier.cs` (même
  garantie d'échec explicite que `ZavuWhatsAppOtpSender` si Zavu non configuré)
- [x] T011 Créer un filtre d'action `AdminApiKeyFilter` (ou middleware) vérifiant le header
  `X-Admin-Api-Key` contre `Admin:ApiKey` — `401` explicite si absent/incorrect (voir `research.md`
  Décision 4)

## Phase 3: User Story 1 - Soumission d'un dossier d'inscription partenaire (Priority: P1) 🎯 MVP

**Goal**: `POST /partners/applications` crée un compte PARTENAIRE + un établissement `EN_ATTENTE`.

**Independent Test**: Scénarios 1 et 2 de `quickstart.md`.

- [x] T012 [US1] Créer `src/KekeBeauty.Application/Onboarding/SubmitPartnerApplicationUseCase.cs` :
  valide les champs obligatoires et les fichiers (FR-003), réutilise `IUtilisateurRepository`
  (déjà générique, feature 003) pour créer/trouver l'utilisateur PARTENAIRE (FR-002, RG-ID-04), crée
  l'établissement `EN_ATTENTE` (FR-004) rattaché à l'`id_commune` du seed, stocke les fichiers via
  `IFileStorage`
- [x] T013 [US1] Créer `src/KekeBeauty.Api/Controllers/PartnersController.cs` : `POST
  /partners/applications` (multipart), mappé selon `contracts/onboarding-api.md` (201/400)
- [x] T014 [US1] Enregistrer les services dans le DI (`Program.cs`) : `IEtablissementRepository`,
  `IFileStorage`, `IPartnerNotifier`, `SubmitPartnerApplicationUseCase`
- [x] T015 [US1] Ajouter un volume nommé `/app/storage` au service `api` dans `docker-compose.yml`
- [x] T016 [US1] Exécuter le Scénario 1 de `quickstart.md` (soumission complète) et confirmer le `201`
- [x] T017 [US1] Exécuter le Scénario 2 de `quickstart.md` (soumission incomplète) et confirmer le `400`

**Checkpoint**: US1 livrable et testable de façon autonome.

## Phase 4: User Story 2 - Consultation et décision administrateur (Priority: P1)

**Goal**: Un administrateur liste, consulte et décide (valide/rejette) un dossier.

**Independent Test**: Scénarios 3, 4 et 5 de `quickstart.md`.

- [x] T018 [US2] Créer `ListPendingApplicationsUseCase.cs` (filtre par statut, FR-005)
- [x] T019 [US2] Créer `ValidateApplicationUseCase.cs` (refuse si photo ou pièce d'identité absente,
  FR-007, Edge Case)
- [x] T020 [US2] Créer `RejectApplicationUseCase.cs` (appelle `IPartnerNotifier`, FR-008/FR-009)
- [x] T021 [US2] Créer `src/KekeBeauty.Api/Controllers/AdminController.cs` : les 4 endpoints
  `/admin/applications/*` protégés par `AdminApiKeyFilter` (T011), mappés selon `contracts/onboarding-api.md`
- [x] T022 [US2] Exécuter le Scénario 3 de `quickstart.md` (liste, détail, fichier, validation)
- [x] T023 [US2] Exécuter le Scénario 4 de `quickstart.md` (rejet + notification)
- [x] T024 [US2] Exécuter le Scénario 5 de `quickstart.md` (401 sans clé admin)

**Checkpoint**: US1 + US2 livrées.

## Phase 5: User Story 3 - Invisibilité tant que non validé (Priority: P2)

**Goal**: Un établissement non `VALIDE` n'apparaît jamais dans une recherche client.

**Independent Test**: Scénario 6 de `quickstart.md`.

- [x] T025 [US3] Exécuté (via requêtes SQL réelles ci-dessus : statut `EN_ATTENTE` → `VALIDE` sur le
  premier dossier, `EN_ATTENTE` → `REJETE` sur le second) ; documenter
  explicitement que le filtrage effectif dans les résultats de recherche sera appliqué par la future
  feature `005-recherche-annuaire` (US-06/US-07), qui MUST filtrer sur `statut_kyc = 'VALIDE'` —
  dépendance à ne pas oublier lors de sa spécification

**Checkpoint**: US1 + US2 + US3 — US3 est une contrainte de conception à respecter par une future
feature plutôt qu'un comportement observable dans celle-ci (aucune recherche client n'existe encore).

## Phase 6: Polish & Cross-Cutting Concerns

- [x] T026 [P] Mettre à jour `README.md` (section API backend) : endpoints partenaire/admin, note sur
  la clé admin temporaire et la dépendance Zavu pour la notification de rejet
- [x] T027 Mettre à jour `docs/scrum/product-backlog.md` (US-04/US-05 → `004-onboarding-partenaire`)
  et créer `docs/scrum/sprint-3.md`

## Dependencies & Execution Order

- Setup (Phase 1, T001-T003) : bloque tout — le seed géographique doit exister avant toute création
  d'établissement.
- Foundational (Phase 2, T004-T011) : bloque les User Stories.
- User Story 1 (Phase 3) : dépend de la Phase 2. MVP.
- User Story 2 (Phase 4) : dépend de la Phase 3 (il faut un dossier soumis pour le consulter/décider).
- User Story 3 (Phase 5) : dépend de la Phase 4 (nécessite une décision pour observer l'état).
- Polish (Phase 6) : après toutes les User Stories.

## Parallel Example

```text
T004, T005, T006, T007 [P] peuvent être écrits en parallèle (interfaces, fichiers distincts)
T003 [P] peut être fait en parallèle de T001/T002 (fichiers de config distincts)
```

## Implementation Strategy

**MVP first** : Phase 3 (US1) avant Phase 4 (US2). US1 seule prouve que les dossiers peuvent être
soumis et stockés correctement ; US2 apporte la boucle de décision complète.
