# Phase 1 — Contracts: API Onboarding Partenaire

## POST /partners/applications (multipart/form-data)

**Champs** : `telephone`, `nomEtablissement`, `gpsLatitude`, `gpsLongitude`, `numeroServiceClient`,
`horaires` (JSON texte), `photoDevanture` (fichier), `pieceIdentite` (fichier)

**Réponses**
- `201 Created` — `{ "idEtablissement": "<uuid>", "statut": "EN_ATTENTE" }` (FR-001/FR-002/FR-004)
- `400 Bad Request` — `{ "status": "invalid_submission", "message": "<champ manquant/invalide>" }` (FR-003)

## GET /admin/applications?statut=EN_ATTENTE (header `X-Admin-Api-Key`)

**Réponses**
- `200 OK` — `[{ "idEtablissement": "<uuid>", "nomEtablissement": "...", "dateCreation": "..." }]` (FR-005)
- `401 Unauthorized` — clé admin absente/incorrecte

## GET /admin/applications/{id} (header `X-Admin-Api-Key`)

**Réponses**
- `200 OK` — détail de l'établissement (sans exposer les fichiers eux-mêmes, juste leur présence) (FR-006)
- `404 Not Found`

## GET /admin/applications/{id}/files/{type} (header `X-Admin-Api-Key`, `type` = `devanture` | `piece-identite`)

**Réponses**
- `200 OK` — flux binaire du fichier (FR-006/FR-011)
- `404 Not Found` — fichier absent

## POST /admin/applications/{id}/validate (header `X-Admin-Api-Key`)

**Réponses**
- `200 OK` — `{ "statut": "VALIDE" }` (FR-007)
- `409 Conflict` — `{ "status": "missing_documents" }` si photo ou pièce d'identité absente (FR-007, Edge Case)

## POST /admin/applications/{id}/reject (header `X-Admin-Api-Key`)

**Réponses**
- `200 OK` — `{ "statut": "REJETE" }` (FR-008) ; déclenche la notification au gérant (FR-009)
