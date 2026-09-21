# Tasks: Gestion d'équipe (collaboratrices)

- [x] T001 Migration `db/migrations/0010_collaborateur.sql`
- [x] T002 Appliquer via `scripts/db/migrate.sh`
- [x] T003 `ICollaborateurRepository` + `CollaborateurRepository` (Dapper)
- [x] T004 `ManageEquipeUseCase`
- [x] T005 `GET/POST/DELETE .../collaborateurs` dans `PartnerManagementController.cs`
- [x] T006 Enregistrer dans `Program.cs`
- [x] T007 DTOs + `PartnerApiClient.GetEquipeAsync/AjouterCollaborateurAsync/RetirerCollaborateurAsync`
- [x] T008 `PartnerEtablissement.razor` : section "Mon équipe" (liste + ajout + retrait)
- [x] T009 Validation curl end-to-end : liste vide, ajout, liste après ajout, 403 non-propriétaire, retrait, 404 idempotent

Toutes les tâches complétées et validées contre la stack Docker réelle.

Hors périmètre (documenté dans spec.md Assumptions) : compte de connexion collaboratrice, planning
individuel, fiches techniques clientes, commissions/pourboires — nécessitent un 3e `TypeCompte` et
des écrans dédiés, hors de ce cycle.
