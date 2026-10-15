# Implementation Plan: Lot 2 — Annuaire

**Spec**: [spec.md](spec.md) | **Constitution**: `.specify/memory/constitution.md`

## Technical Context
- Les entités de catalogue vivent dans `KekeBeauty.Modules.Catalog` ; les horaires dans `KekeBeauty.Modules.Scheduling`. Elles n'ont **pas de navigation EF vers Geography** : `CommuneId` est un simple identifiant.
- Les lectures qui croisent plusieurs modules (recherche, fiche) sont des **requêtes en lecture seule** dans `KekeBeauty.Infrastructure/Search`, seul endroit qui voit tous les schémas (aucune écriture inter-modules).
- `unaccent` est exposé à EF par une fonction `PostgresFunctions.Unaccent` ; la distance utilise `Point.Distance` / `IsWithinDistance` sur `geography`.
- Médias : `storage_key` relatif résolu par `Media:BaseUrl` (`/media` en dev, servi par nginx) ; le stockage objet (MinIO) arrive au lot 3.
- Frontend : état des filtres dans l'URL ; Leaflet chargé à la demande (import dynamique) ; `DirectionsLinkService` à gabarits configurables.

## Décisions
1. Seed de démo séparé du seed de référence (`Database:SeedDemoData`), jamais actif en production.
2. Statistiques : `INSERT … ON CONFLICT DO UPDATE` atomique, endpoint public avec limitation de débit.
3. `owner_user_id` nullable (les établissements de démo n'ont pas de propriétaire ; le lot 3 l'imposera à l'onboarding).
