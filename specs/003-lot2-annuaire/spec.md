# Feature Specification: Lot 2 — Annuaire et fiche établissement

**Feature Branch**: `003-lot2-annuaire` | **Created**: 2026-10-11 | **Status**: In progress
**Input**: PRD §7.2 (SRCH-01 à 05) et §7.3 (EST-01 à 06), sur **données de démonstration**.

## Décisions
- Carte : **OpenStreetMap + Leaflet** (gratuit, sans clé) ; le PRD §10.3 autorise un autre fournisseur par configuration plus tard.
- Médias de démonstration : **visuels SVG générés** aux couleurs de la marque (aucun droit d'auteur).
- Livraison de la **vue carte dès ce lot** (SRCH-05).
- **Yango** (EST-04) : le format du lien profond n'est documenté nulle part (PRD Q4). Gabarit **configurable** (`yangoUrlTemplate`, variable `YANGO_URL_TEMPLATE`) ; l'option est masquée tant qu'il n'est pas renseigné.
- Open Graph (EST-06) : balises posées côté navigateur ; un aperçu fiable pour les robots (WhatsApp, Facebook) exige du prérendu → **lot 7**.

## User Scenarios & Testing

### US1 — Chercher par catégorie, lieu et texte (P1)
Depuis l'accueil ou `/etablissements`, je filtre par catégorie, par Région > Ville > Commune (listes en cascade) et par texte (nom d'établissement ou de prestation, insensible aux accents et à la casse). Les filtres vivent dans l'URL (partageable).
1. Seuls les établissements **actifs** apparaissent.
2. Les résultats sont paginés (« Voir plus »), triés : mis en avant d'abord, puis distance si connue, sinon ordre alphabétique.
3. Filtre « Réservation en ligne disponible ».

### US2 — Chercher autour de moi (P1)
Avec l'accord de géolocalisation, je vois les établissements triés par distance, filtrables à 1, 3, 5, 10 ou 25 km (PostGIS `ST_DWithin`, index GiST).
1. Refus ou indisponibilité de la géolocalisation : message clair, la recherche classique reste utilisable.

### US3 — Résultats en liste ou sur carte (P1)
Cartes visuelles (photo, nom, catégorie, commune, distance, « à partir de ») ou carte OpenStreetMap avec un repère par établissement ; un clic ouvre un aperçu puis la fiche.

### US4 — Fiche établissement (P1)
Carrousel photos/vidéos, description, catégories, adresse et commune, horaires, prestations groupées avec prix (« à partir de ») et durée, boutons **Appeler**, **S'y rendre** (Google Maps, Apple Maps, Yango si configuré) et **Réserver** (si la réservation en ligne est disponible). URL lisible `/etablissements/{slug}`.
1. Clics sur Appeler / S'y rendre comptés dans les statistiques du partenaire ; vue de fiche comptée.
2. Établissement inactif ou inconnu : page « introuvable ».

## Requirements
- **FR-001** Modèle : `catalog.establishments` (+ catégories, médias, groupes de prestations, prestations, statistiques journalières), `scheduling.opening_hours`.
- **FR-002** `GET /api/v1/establishments` (filtres `category`, `cityId`, `communeId`, `lat`, `lng`, `radiusKm`, `q`, `bookable`, `sort`, `page`, `pageSize` ≤ 50).
- **FR-003** `GET /api/v1/establishments/{slug}` et `POST /api/v1/establishments/{id}/events` (`view` | `call` | `directions`, limité en débit).
- **FR-004** Recherche textuelle sans accents (`unaccent`), proximité PostGIS sur `geography(Point,4326)`.
- **FR-005** Jeu de démonstration : 10 établissements fictifs à Abidjan (catégories, prestations, horaires, médias), idempotent, activé par `Database:SeedDemoData`.
- **FR-006** Frontend : accueil → recherche, page résultats (liste/carte), fiche, service de liens d'itinéraire, géolocalisation.
- **FR-007** Accessibilité WCAG 2.1 AA, mobile-first, `prefers-reduced-motion`.

## Success Criteria
- **SC-001** Un client trouve un établissement par catégorie et par zone en moins de 3 interactions.
- **SC-002** Recherche : p95 < 300 ms sur le jeu de démonstration (hors réseau).
- **SC-003** Tests : filtres, proximité, accents, pagination, événements, 404 sur établissement inactif.
