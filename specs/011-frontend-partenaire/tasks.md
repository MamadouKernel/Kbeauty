# Tasks: Frontend partenaire — gestion établissement et RDV

**Input**: `specs/011-frontend-partenaire/{plan.md, spec.md, research.md, contracts/, quickstart.md}`

## Phase 1: Fondations backend (bloquant)
- [ ] T001 [P] `IPartnerPrestationRepository.GetEtablissementsByGerantAsync` + implémentation Dapper
- [ ] T002 [P] `IRdvRepository.ListByEtablissementAsync` + implémentation Dapper
- [ ] T003 `GET partenaire/etablissements` dans `PartnerManagementController.cs` (nouveau contrôleur ou route dédiée)
- [ ] T004 `GET partenaire/etablissements/{id}/rdv` dans `RdvController.cs`, protégé par `PartnerOwnershipFilter`

## Phase 2: User Story 1 — Authentification gérant (P1)
- [ ] T005 [US1] `Models/PartnerApiDtos.cs`
- [ ] T006 [US1] `Services/PartnerApiClient.cs` — `GetMesEtablissementsAsync`
- [ ] T007 [US1] `Components/Pages/PartnerLogin.razor` (réutilise `AuthApiClient`/`ClientSessionService` existants, type PARTENAIRE)

## Phase 3: User Story 2 — Prestations et catégories (P1)
- [ ] T008 [US2] `Services/PartnerApiClient.cs` — `AddPrestationAsync`, `UpdatePrestationAsync`, `DeletePrestationAsync`, `AssignCategoryAsync`
- [ ] T009 [US2] `Components/Pages/PartnerDashboard.razor` — liste établissements
- [ ] T010 [US2] `Components/Pages/PartnerEtablissement.razor` — prestations + catégories, gestion `403`

## Phase 4: User Story 3 — Demandes de RDV (P1)
- [ ] T011 [US3] `Services/PartnerRdvApiClient.cs` — `ListAsync`, `ConfirmerAsync`, `RefuserAsync`, `ReprogrammerAsync`
- [ ] T012 [US3] `Components/Pages/PartnerRdv.razor` — liste + décisions

## Phase 5: Intégration & Polish
- [ ] T013 Tester tous les scénarios de `quickstart.md` en réel
- [ ] T014 `dotnet build` final, mettre à jour `README.md`
