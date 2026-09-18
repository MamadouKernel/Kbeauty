# Tasks: Frontend partenaire — gestion établissement et RDV

**Input**: `specs/011-frontend-partenaire/{plan.md, spec.md, research.md, contracts/, quickstart.md}`

## Phase 1: Fondations backend (bloquant)
- [x] T001 [P] `IPartnerPrestationRepository.GetEtablissementsByGerantAsync` + implémentation Dapper
- [x] T002 [P] `IRdvRepository.ListByEtablissementAsync` + implémentation Dapper
- [x] T003 `GET partenaire/etablissements` dans `PartnerManagementController.cs` (nouveau contrôleur ou route dédiée)
- [x] T004 `GET partenaire/etablissements/{id}/rdv` dans `RdvController.cs`, protégé par `PartnerOwnershipFilter`

## Phase 2: User Story 1 — Authentification gérant (P1)
- [x] T005 [US1] `Models/PartnerApiDtos.cs`
- [x] T006 [US1] `Services/PartnerApiClient.cs` — `GetMesEtablissementsAsync`
- [x] T007 [US1] `Components/Pages/PartnerLogin.razor` (réutilise `AuthApiClient`/`ClientSessionService` existants, type PARTENAIRE)

## Phase 3: User Story 2 — Prestations et catégories (P1)
- [x] T008 [US2] `Services/PartnerApiClient.cs` — `AddPrestationAsync`, `UpdatePrestationAsync`, `DeletePrestationAsync`, `AssignCategoryAsync`
- [x] T009 [US2] `Components/Pages/PartnerDashboard.razor` — liste établissements
- [x] T010 [US2] `Components/Pages/PartnerEtablissement.razor` — prestations + catégories, gestion `403`

## Phase 4: User Story 3 — Demandes de RDV (P1)
- [x] T011 [US3] `Services/PartnerRdvApiClient.cs` — `ListAsync`, `ConfirmerAsync`, `RefuserAsync`, `ReprogrammerAsync`
- [x] T012 [US3] `Components/Pages/PartnerRdv.razor` — liste + décisions

## Phase 5: Intégration & Polish
- [x] T013 Tester tous les scénarios de `quickstart.md` en réel
- [x] T014 `dotnet build` final, mettre à jour `README.md`
