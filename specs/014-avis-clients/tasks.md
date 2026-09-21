# Tasks: Avis clients après rendez-vous

- [x] T001 Migration `db/migrations/0009_avis.sql` (table `avis`, contenu dans `data-model.md`)
- [x] T002 Appliquer via `scripts/db/migrate.sh`
- [x] T003 [P] `IAvisRepository` dans `src/KekeBeauty.Application/Rdv/IAvisRepository.cs`
- [x] T004 Implémenter `AvisRepository` (Dapper) dans `src/KekeBeauty.Infrastructure/Rdv/AvisRepository.cs`
- [x] T005 `LaisserAvisUseCase` dans `src/KekeBeauty.Application/Rdv/LaisserAvisUseCase.cs`
- [x] T006 [US1] `POST /rdv/{id}/avis` dans `RdvController.cs`
- [x] T007 [US2] `GET /etablissements/{id}/avis` dans `RdvController.cs`
- [x] T008 [US1] Étendre `IRdvRepository.ListByClientAsync`/`RdvHistoriqueRow` avec `ADejaAvis`
- [x] T009 Enregistrer `IAvisRepository` + `LaisserAvisUseCase` dans `Program.cs`
- [x] T010 [US1] DTOs + `RdvApiClient.LaisserAvisAsync` côté Web
- [x] T011 [US2] DTOs + `DirectoryApiClient.GetAvisAsync` (ou `RdvApiClient`) côté Web
- [x] T012 [US1] `MesRendezVous.razor` : bouton "Laisser un avis" sur RDV TERMINE sans avis (mini-formulaire étoiles + commentaire)
- [x] T013 [US2] `EtablissementDetailPage.razor` : onglet Avis réel (note moyenne + liste), remplace le placeholder
- [x] T014 Build + validation curl end-to-end (idempotence 409, 404 non-propriétaire, moyenne correcte)
- [x] T015 Mise à jour `docs/scrum/product-backlog.md`

**MVP** = T001-T009 + T012 (US1 seule, laisser un avis). US2 (affichage) suit immédiatement pour boucler la boucle.
