# API Contract: Billing (Abonnement et Paiement)

## POST /api/etablissements/{id}/abonnements
Souscrit un abonnement pour l'établissement `{id}`.
**Auth**: header `X-Partner-Id` (guid), vérifié par `PartnerOwnershipFilter` (existant, 006/007).

**Request**:
```json
{ "periodicite": "MENSUEL|ANNUEL", "canal": "WAVE|ORANGE_MONEY|MTN|MOOV|VISA|MASTERCARD" }
```

**Responses**:
- `201 Created` — paiement réussi. Body: `{ "idAbonnement", "statut": "ACTIF", "statutTransaction": "REUSSIE" }`
- `201 Created` — paiement échoué (agrégateur non configuré/refus). Body: `{ "idAbonnement", "statut": "IMPAYE", "statutTransaction": "ECHOUEE" }` (créé quand même — FR-002, jamais un succès trompeur mais pas un blocage silencieux)
- `409 Conflict` — un abonnement `ACTIF` existe déjà pour cet établissement (FR-004)
- `401 Unauthorized` — header `X-Partner-Id` absent/malformé
- `403 Forbidden` — établissement non possédé par ce gérant (FR-003)

## GET /api/admin/abonnements?statut={statut}
Liste les abonnements filtrés par statut.
**Auth**: header `Admin:ApiKey` (existant, `AdminApiKeyFilter`, 004).

**Responses**:
- `200 OK` — `[{ "idAbonnement", "idEtablissement", "periodicite", "montant", "statut" }, ...]`
- `401 Unauthorized` — clé admin absente/invalide

## POST /api/admin/abonnements/{id}/relance
Déclenche une tentative de notification de relance sur un abonnement `IMPAYE`.
**Auth**: `Admin:ApiKey`.

**Responses**:
- `200 OK` — `{ "notificationEnvoyee": true|false }` (reflète le résultat réel, échec explicite si Zavu non configuré)
- `404 Not Found` — abonnement introuvable
- `409 Conflict` — l'abonnement n'est pas `IMPAYE`

## GET /api/admin/tarifs
Consulte le tarif standard par périodicité.
**Auth**: `Admin:ApiKey`.

**Responses**:
- `200 OK` — `[{ "periodicite": "MENSUEL", "montant": ... }, { "periodicite": "ANNUEL", "montant": ... }]`

## PUT /api/admin/tarifs/{periodicite}
Modifie le tarif standard d'une périodicité (n'affecte que les souscriptions futures — FR-007).
**Auth**: `Admin:ApiKey`.

**Request**: `{ "montant": number }`

**Responses**:
- `200 OK` — `{ "periodicite", "montant" }`
- `400 Bad Request` — montant invalide (<= 0)
