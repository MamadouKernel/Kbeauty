# Phase 1 — Contracts: API Annuaire (Recherche & Fiche)

## GET /etablissements?categorie=<libelle>&commune=<libelle> (public)

**Réponses**
- `200 OK` — `[{ "idEtablissement": "<uuid>", "nomEtablissement": "...", "libelleCommune": "...", "categories": ["Spa"] }]`
  (liste vide si aucun résultat, FR-003)

## GET /etablissements/{id} (public)

**Réponses**
- `200 OK` — `{ "idEtablissement": "...", "nomEtablissement": "...", "description": "...", "numeroServiceClient": "...", "lienItineraire": "https://www.google.com/maps/dir/?api=1&destination=...", "medias": [...], "prestations": [...] }`
- `404 Not Found` — établissement inexistant OU non validé (même réponse, FR-007)

## POST /admin/applications/{id}/categories (header `X-Admin-Api-Key`) — FR-008, mécanisme minimal

**Requête** : `{ "libelleCategorie": "Spa" }`

**Réponses**
- `200 OK` — `{ "idCategorie": "<uuid>", "libelleCategorie": "Spa" }`
- `401 Unauthorized` — clé admin absente/incorrecte
- `404 Not Found` — établissement inexistant

## POST /admin/applications/{id}/prestations (header `X-Admin-Api-Key`) — FR-008, mécanisme minimal

**Requête** : `{ "libellePrestation": "Massage relaxant", "tarif": 15000, "dureeMinutes": 60 }`

**Réponses**
- `201 Created` — `{ "idPrestation": "<uuid>" }`
- `401 Unauthorized`
- `404 Not Found` — établissement inexistant
