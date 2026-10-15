# Tasks: Lot 6 — Back-office complet

## Backend
- [x] T601 Audit : `Audit.Add` partout, `AuditQueryService` filtrable, journal des modifications de formules
- [x] T602 Utilisateurs : recherche, détail, suspension/réactivation avec motif, révocation des sessions, règles Admin/SuperAdmin
- [x] T603 Administrateurs (SuperAdmin) : nommer, retirer, liste
- [x] T604 Modération des fiches : suspension/rétablissement, retrait de photo, historique, notification du propriétaire
- [x] T605 Finances : synthèse (plans, MRR, encaissements par moyen, impayés), relance manuelle, export CSV
- [x] T606 Paramétrage : catégories, zones, délai de grâce (`app_settings`) utilisé par la tâche de facturation
- [x] T607 Modèles de messages éditables (`notification_templates`) appliqués aux notifications de rendez-vous et de facturation
- [x] T608 Migration `AddBackoffice`, endpoints `/api/v1/admin/*`, politique `SuperAdmin`
- [x] T609 Tests : 120 unitaires, 107 d'intégration

## Frontend
- [x] T611 `AdminApiService`, coque d'administration avec navigation, accueil à cartes
- [x] T612 Pages utilisateurs, établissements, finances, journal, administrateurs
- [x] T613 Paramètres : formules, catégories, zones, messages, général
- [x] T614 Tests Vitest (120 au total)

## Limites connues
- Les messages personnalisés ne s'appliquent qu'aux notifications qui fournissent leurs variables (rendez-vous et facturation).
- Le corps des e-mails de relance suit le texte d'origine.
