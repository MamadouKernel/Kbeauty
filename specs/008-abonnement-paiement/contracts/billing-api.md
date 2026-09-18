# API Contract: Billing (Abonnement et Paiement)

## POST /api/etablissements/{id}/abonnements
Souscrit un abonnement pour l'établissement `{id}`. WiniPayer fonctionne par lien de paiement
hébergé (pas de paiement synchrone) : la souscription crée l'abonnement `IMPAYE` et retourne un
`checkoutUrl` vers lequel rediriger le client ; le paiement réel arrive plus tard via
`POST /webhooks/winipayer/callback` (voir Décision 4 mise à jour, remplacement CinetPay→WiniPayer).
**Auth**: header `X-Partner-Id` (guid), vérifié par `PartnerOwnershipFilter` (existant, 006/007).

**Request**:
```json
{ "periodicite": "MENSUEL|ANNUEL" }
```

**Responses**:
- `201 Created` — lien de paiement généré. Body: `{ "idAbonnement", "statut": "IMPAYE", "checkoutUrl" }`
- `409 Conflict` — un abonnement `ACTIF` existe déjà pour cet établissement (FR-004)
- `502 Bad Gateway` — échec explicite si le compte marchand WiniPayer n'est pas configuré ou refuse la demande
- `401 Unauthorized` — header `X-Partner-Id` absent/malformé
- `403 Forbidden` — établissement non possédé par ce gérant (FR-003)

## POST /webhooks/winipayer/callback
Notification WiniPayer du résultat réel du paiement (`callback_url` fourni à la création du lien).
Le `hash` reçu est vérifié (`sha256(privateKey + uuid + crypto + amount + created_at)`) avant toute
mise à jour ; idempotent (un callback rejoué n'a aucun effet supplémentaire).

**Responses**:
- `200 OK` — `{ "status": "applied" }` ou `{ "status": "already_processed_or_unknown" }`
- `400 Bad Request` — payload incomplet
- `401 Unauthorized` — signature invalide
- `503 Service Unavailable` — clé privée non configurée

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
