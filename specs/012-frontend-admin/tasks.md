# Tasks: Frontend admin — KYC, modération et abonnements

**Input**: `specs/012-frontend-admin/{plan.md, spec.md, research.md, quickstart.md}`

## Phase 1: Fondations (bloquant)
- [x] T001 [P] `Models/AdminApiDtos.cs`
- [x] T002 [P] `Services/AdminSessionService.cs` — clé API en `ProtectedLocalStorage`

## Phase 2: User Story 1 — Connexion admin (P1)
- [x] T003 [US1] `Services/AdminApiClient.cs` — méthode de vérification (appel `GET /admin/abonnements` ou équivalent avec la clé fournie, `401` = invalide)
- [x] T004 [US1] `Components/Pages/AdminLogin.razor`

## Phase 3: User Story 2 — Dossiers KYC (P1)
- [x] T005 [US2] `Services/AdminApiClient.cs` — `ListDossiersAsync`, `GetDossierAsync`, `ValiderAsync`, `RejeterAsync`
- [x] T006 [US2] `Components/Pages/AdminDossiers.razor` — liste + filtre statut
- [x] T007 [US2] `Components/Pages/AdminDossierDetail.razor` — détail + valider/rejeter, message explicite sur `409`

## Phase 4: User Story 3 — Modération (P1)
- [x] T008 [US3] `Services/AdminApiClient.cs` — `SuspendreUtilisateurAsync`, `ReactiverUtilisateurAsync`, `SuspendreEtablissementAsync`, `ReactiverEtablissementAsync`
- [x] T009 [US3] `Components/Pages/AdminModeration.razor` — saisie d'identifiant, actions, message "introuvable"

## Phase 5: User Story 4 — Abonnements (P2)
- [x] T010 [US4] `Services/AdminApiClient.cs` — `ListAbonnementsAsync`, `RelancerAsync`, `ListerTarifsAsync`, `UpdateTarifAsync`
- [x] T011 [US4] `Components/Pages/AdminAbonnements.razor` — liste + filtre + relance + tarifs

## Phase 6: Intégration & Polish
- [x] T012 Tester tous les scénarios de `quickstart.md` en réel
- [x] T013 `dotnet build` final, mettre à jour `README.md`
