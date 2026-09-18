# Tasks: Modération back-office

**Input**: `specs/009-moderation-back-office/{plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md}`

## Phase 1: Fondations (bloquant)
- [x] T001 Migration `db/migrations/0006_moderation.sql` — `est_suspendu BOOLEAN NOT NULL DEFAULT FALSE` sur `utilisateur` et `etablissement`
- [x] T002 [P] `src/KekeBeauty.Application/Moderation/IModerationRepository.cs`
- [x] T003 [P] `src/KekeBeauty.Infrastructure/Moderation/ModerationRepository.cs`

## Phase 2: User Story 1 — Suspension et réactivation admin (P1)
- [x] T004 [US1] `src/KekeBeauty.Application/Moderation/SuspendUtilisateurUseCase.cs`
- [x] T005 [US1] `src/KekeBeauty.Application/Moderation/SuspendEtablissementUseCase.cs`
- [x] T006 [US1] `ModerationController.cs` — 4 endpoints, protégés par `AdminApiKeyFilter` (existant), `404` si introuvable (FR-004)

## Phase 3: User Story 2 — Effets sur les fonctionnalités (P1)
- [x] T007 [US2] `Domain/Entities/Utilisateur.cs` + `UtilisateurRepository.cs` — ajout `EstSuspendu`
- [x] T008 [US2] `RequestOtpUseCase.cs` — refuse (`account_suspended`) si compte suspendu (FR-005) ; `AuthController.cs` → `403`
- [x] T009 [US2] `DirectoryRepository.cs` — exclut `est_suspendu = true` de la recherche et de la fiche (FR-006)
- [x] T010 [US2] `RdvRepository.cs` — refuse la création de RDV si établissement suspendu (FR-007)
- [x] T011 [US2] `AbonnementRepository.cs` — refuse la souscription si établissement suspendu (FR-008)

## Phase 4: Intégration & Polish
- [x] T012 DI wiring dans `Program.cs`
- [x] T013 Tester tous les scénarios de `quickstart.md` contre le container Docker réel via curl
- [x] T014 Corriger la ligne US-19 dans `docs/scrum/product-backlog.md` → `009-moderation-back-office`
- [x] T015 Créer `docs/scrum/sprint-8.md`
- [x] T016 `dotnet build` final, mettre à jour `README.md`
