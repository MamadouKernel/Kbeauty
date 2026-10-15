# Tasks: Lot 2 — Annuaire

## Backend
- [x] T201 Domaine Catalog : Establishment, EstablishmentCategory, Media, ServiceGroup, Service, EstablishmentStatsDaily (+ configurations, index GiST et trigramme ; latitude/longitude en colonnes générées)
- [x] T202 Domaine Scheduling : OpeningHour (+ configuration)
- [x] T203 Requêtes lecture seule : recherche (filtres, accents, proximité, pagination) et fiche
- [x] T204 Endpoints `/api/v1/establishments` (liste, fiche, événements limités en débit) avec contrat OpenAPI typé
- [x] T205 Seed de démonstration (10 établissements, idempotent) + migration `AddCatalog`
- [x] T206 Tests d'intégration (filtres, proximité, accents, jokers LIKE, pagination, validation, fiche, événements, 404) — 24 tests

## Frontend
- [x] T211 Visuels de démonstration générés (21 SVG) servis sous `/media/demo` (`scripts/generate-demo-media.cjs`)
- [x] T212 Services : établissements, géolocalisation, liens d'itinéraire, formats (+ tests)
- [x] T213 Page résultats : filtres en cascade, « autour de moi », liste, pagination, état dans l'URL
- [x] T214 Vue carte Leaflet / OpenStreetMap (repères, aperçu, popups sans HTML injecté)
- [x] T215 Fiche : carrousel médias, horaires, prestations, Appeler, S'y rendre, Réserver, balises Open Graph
- [x] T216 Accueil → recherche ; tests Vitest (62) ; parcours réel dans le navigateur

## Limites connues
- **Yango** : gabarit à fournir via `YANGO_URL_TEMPLATE` (PRD Q4) ; option masquée tant qu'il manque.
- **Aperçus Open Graph** pour les robots (WhatsApp, Facebook) : prérendu au lot 7 (balises posées côté navigateur seulement).
- **Réservation** : le bouton « Réserver » affiche un message ; le parcours arrive au lot 4.
- **Vue carte** : couverte par le parcours réel dans le navigateur, pas par un test automatisé (Leaflet exige un vrai DOM/réseau).
- **Médias** : visuels d'illustration générés ; vidéos et téléversement réel (MinIO) au lot 3.
- **Performance** : p95 < 300 ms non mesuré sur un gros volume (seulement 10 établissements de démonstration).
