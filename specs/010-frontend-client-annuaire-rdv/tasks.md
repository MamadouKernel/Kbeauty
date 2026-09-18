# Tasks: Frontend client — annuaire et prise de RDV

**Input**: `specs/010-frontend-client-annuaire-rdv/{plan.md, spec.md, research.md, contracts/, quickstart.md}`

## Phase 1: Fondations (bloquant)
- [x] T001 Créer le projet `src/KekeBeauty.Web` (Blazor Web App, .NET 8, render mode Server), l'ajouter à `KekeBeauty.slnx`
- [x] T002 [P] Backend : `IRdvRepository.GetStatutAsync(idRdv, idClient)` + implémentation Dapper (`RdvRepository.cs`)
- [x] T003 [P] Backend : `GET /rdv/{id}` dans `RdvController.cs` (401/403/404, cf. `contracts/rdv-status-api.md`)
- [x] T004 `Program.cs` (Web) — `HttpClient` nommé "Api", config `Api:BaseUrl` (`appsettings.json`)

## Phase 2: User Story 1 — Recherche et fiche établissement (P1)
- [x] T005 [US1] `Services/DirectoryApiClient.cs` — `SearchAsync(categorie?, commune?)`, `GetDetailAsync(id)`
- [x] T006 [US1] `Components/Pages/Home.razor` — filtres catégorie/commune, liste résultats, message "aucun résultat"
- [x] T007 [US1] `Components/Pages/EtablissementDetailPage.razor` (renommé pour éviter la collision avec `Models.EtablissementDetail`, même pattern que `Infrastructure.Listing`/`System.IO.Directory`) — médias, prestations, lien `tel:`, lien itinéraire, `404` géré explicitement

## Phase 3: User Story 2 — Authentification OTP (P1)
- [x] T008 [US2] `Services/AuthApiClient.cs` — `RequestOtpAsync`, `VerifyOtpAsync`
- [x] T009 [US2] `Services/ClientSessionService.cs` — lecture/écriture `idUtilisateur` en `ProtectedLocalStorage`
- [x] T010 [US2] `Components/Pages/Login.razor` — formulaire téléphone → code, messages d'erreur explicites (403/502/400)

## Phase 4: User Story 3 — Prise de RDV (P1)
- [x] T011 [US3] `Services/RdvApiClient.cs` — `GetCreneauxAsync`, `RequestRdvAsync`, `GetStatutAsync`
- [x] T012 [US3] `Components/Pages/PrendreRdv.razor` — calendrier créneaux occupés, sélection prestation/créneau, soumission, affichage statut, redirection vers `/login` si non authentifié

## Phase 5: Intégration & Polish
- [x] T013 Gestion d'erreur transverse (FR-008) — composant/état d'erreur réutilisé sur toutes les pages en cas d'échec HTTP
- [x] T014 `docker-compose.yml` — service `web` (Dockerfile multi-stage, port dédié `WEB_PORT`)
- [x] T015 Tester tous les scénarios de `quickstart.md` en réel (navigateur + curl pour simuler les décisions gérant)
- [x] T016 `dotnet build` final, mettre à jour `README.md`
