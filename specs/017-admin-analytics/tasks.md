# Tasks: Tableau de bord global Super-Admin

- [x] T001 `IAdminStatsRepository` + `AdminStatsRepository` (Dapper, agrégations réelles)
- [x] T002 `AdminAnalyticsController` (`GET /admin/statistiques`, protégé par `AdminApiKeyFilter`)
- [x] T003 Enregistrer dans `Program.cs`
- [x] T004 DTOs + `AdminApiClient.GetStatistiquesGlobalesAsync` côté Web
- [x] T005 `AdminDashboard.razor` (`/admin`) : tableau de bord temps réel (établissements, RDV, revenus, abonnements, communauté)
- [x] T006 Validation curl (401 sans clé, 200 avec clé, valeurs réelles) + validation navigateur (connexion admin réelle, données affichées correctes)

Toutes les tâches complétées et validées end-to-end (curl + navigateur, connexion admin réelle réussie).

Hors périmètre (documenté dans spec.md Assumptions) : litiges, acomptes, conciergerie — aucune entité
modélisée, nécessiterait son propre cycle spec Kit si le besoin se confirme.
