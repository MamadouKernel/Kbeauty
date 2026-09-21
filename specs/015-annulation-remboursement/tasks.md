# Tasks: Annulation client & retry paiement (Kéké Protect)

- [x] T001 [US1] `IRdvPaiementRepository.RelancerAsync` + `RdvPaiementRepository` (Dapper)
- [x] T002 [US2] `IRdvPaiementRepository.MarquerRembourseAsync` + implémentation
- [x] T003 [US2] `IRdvRepository.AnnulerParClientAsync` + `RdvRepository` (Dapper)
- [x] T004 [US1] `RelancerPaiementRdvUseCase`
- [x] T005 [US2] `AnnulerRdvUseCase` (annule + marque REMBOURSEE si paiement REUSSIE)
- [x] T006 [US1] `POST /rdv/{id}/paiement/relancer` dans `RdvController.cs`
- [x] T007 [US2] `POST /rdv/{id}/annuler` dans `RdvController.cs`
- [x] T008 Enregistrer les 2 use cases dans `Program.cs`
- [x] T009 [US1] `RdvApiClient.RelancerPaiementAsync` côté Web
- [x] T010 [US2] `RdvApiClient.AnnulerAsync` côté Web
- [x] T011 `MesRendezVous.razor` : boutons "Annuler" (DEMANDE/CONFIRME) et "Réessayer" (paiement ECHOUEE)
- [x] T012 Validation curl end-to-end : annulation simple, annulation avec remboursement (REUSSIE→REMBOURSEE), relance paiement (lien WinPayer TEST réel généré)

Toutes les tâches complétées et validées contre la stack Docker réelle (pas de mock).
