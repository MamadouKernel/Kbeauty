---
description: "Task list template for feature implementation"
---

# Tasks: Prise de Rendez-vous

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

## Phase 1: Foundational

- [x] T001 [P] Créer `src/KekeBeauty.Application/Rdv/Dtos.cs` (`CreneauOccupe`, `RequestRdvResult`, `DecideRdvResult`)
- [x] T002 [P] Créer `src/KekeBeauty.Application/Rdv/IRdvRepository.cs` (créneaux occupés par date,
  création avec vérification atomique de chevauchement, décision confirmer/refuser/reprogrammer,
  `GetOwnerIdAsync`/`GetClientTelephoneAsync`)
- [x] T003 [P] Créer `src/KekeBeauty.Application/Rdv/IRdvNotifier.cs`
- [x] T004 Créer `src/KekeBeauty.Infrastructure/Rdv/RdvRepository.cs` (Dapper ; `INSERT ... WHERE NOT
  EXISTS` pour la création, voir `research.md` Décision 1)
- [x] T005 Créer `src/KekeBeauty.Infrastructure/Rdv/ZavuWhatsAppRdvNotifier.cs`

## Phase 2: US1 - Créneaux occupés (P1) 🎯 MVP

- [x] T006 [US1] Créer `RequestRdvUseCase.cs` (partie lecture : créneaux occupés)
- [x] T007 [US1] Créer `src/KekeBeauty.Api/Controllers/RdvController.cs` : `GET
  /etablissements/{id}/creneaux?date=` (public)
- [x] T008 [US1] Exécuter Scénario 1 de `quickstart.md`

## Phase 3: US2 - Demande de rendez-vous (P1)

- [x] T009 [US2] Compléter `RequestRdvUseCase.cs` (partie création, `X-Client-Id`)
- [x] T010 [US2] Ajouter `POST /rdv` au contrôleur (lit `X-Client-Id`, `409` si chevauchement/refus)
- [x] T011 [US2] Enregistrer les services dans le DI (`Program.cs`)
- [x] T012 [US2] Exécuter Scénario 2 de `quickstart.md` (création puis chevauchement `409`)

## Phase 4: US3 - Décision du gérant et notification (P1)

- [x] T013 [US3] Créer `DecideRdvUseCase.cs` (confirmer/refuser/reprogrammer + appel `IRdvNotifier`)
- [x] T014 [US3] Ajouter `POST .../rdv/{idRdv}/confirmer|refuser|reprogrammer` au contrôleur,
  protégés par `PartnerOwnershipFilter` (006)
- [x] T015 [US3] Exécuter Scénario 3 de `quickstart.md`
- [x] T016 [US3] Exécuter Scénario 4 (`403` sur établissement non possédé)

## Phase 5: Polish
- [x] T017 [P] Mettre à jour `README.md`, `docs/scrum/product-backlog.md` (US-12 à US-14 →
  `007-prise-rdv`) et créer `docs/scrum/sprint-6.md`
