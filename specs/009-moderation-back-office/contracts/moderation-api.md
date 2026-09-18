# API Contract: Modération back-office

Toutes les routes sont protégées par `Admin:ApiKey` (header `X-Admin-Api-Key`, existant, 004).

## POST /admin/utilisateurs/{id}/suspendre
Suspend le compte (idempotent — FR-009).
- `200 OK` — `{ "idUtilisateur", "estSuspendu": true }`
- `404 Not Found` — identifiant inexistant (FR-004)

## POST /admin/utilisateurs/{id}/reactiver
Réactive le compte (idempotent).
- `200 OK` — `{ "idUtilisateur", "estSuspendu": false }`
- `404 Not Found`

## POST /admin/etablissements/{id}/suspendre
Suspend l'établissement (idempotent). N'affecte pas `statutKyc`.
- `200 OK` — `{ "idEtablissement", "estSuspendu": true }`
- `404 Not Found`

## POST /admin/etablissements/{id}/reactiver
Réactive l'établissement (idempotent).
- `200 OK` — `{ "idEtablissement", "estSuspendu": false }`
- `404 Not Found`

## Effets observables (pas de nouvel endpoint, comportement modifié)
- `POST /auth/otp/request` sur un compte suspendu → `403 Forbidden`, `{ "status": "account_suspended" }` (FR-005)
- `GET /etablissements` / `GET /etablissements/{id}` — un établissement suspendu n'apparaît plus (FR-006)
- `POST /rdv` sur un établissement suspendu → refus (même statut que prestation/établissement introuvable, FR-007)
- `POST /etablissements/{id}/abonnements` sur un établissement suspendu → `409 Conflict` (FR-008)
