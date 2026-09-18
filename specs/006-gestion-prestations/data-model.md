# Phase 1 — Data Model: Gestion des Prestations et Catégories par le Partenaire

Aucune nouvelle table. Réutilise `etablissement` (vérification `id_utilisateur_gerant`), `prestation`
(CRUD complet), `categorie`/`etablissement_categorie` (assign/remove).

## Règle appliquée
- FR-004 : toute mutation `prestation`/`etablissement_categorie` MUST être précédée d'une vérification
  `etablissement.id_utilisateur_gerant = X-Partner-Id`, sinon `403`.
