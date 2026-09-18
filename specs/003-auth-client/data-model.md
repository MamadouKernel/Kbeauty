# Phase 1 — Data Model: Inscription et Authentification Client par Téléphone

## Entité technique ajoutée

### otp_challenge

| Colonne | Type | Règle |
|---|---|---|
| id | UUID PK | `gen_random_uuid()` |
| telephone | VARCHAR(20) | Format E.164, non vide |
| type_compte | type_compte_enum | `CLIENT` pour cette feature (réutilise l'enum existant) |
| code_hash | VARCHAR(64) | Hash SHA-256 du code à 6 chiffres (FR-010 — jamais en clair) |
| expires_at | TIMESTAMPTZ | `created_at` + 5 minutes (FR-005) |
| status | otp_status_enum | `PENDING` / `CONSUMED` / `INVALIDATED` |
| created_at | TIMESTAMPTZ | Défaut `now()` |

Index : `(telephone, type_compte, status)` pour retrouver rapidement le challenge `PENDING` actif.

Règles associées :
- **FR-005/FR-006** : à la création d'un nouveau challenge pour `(telephone, type_compte)`, tout
  challenge `PENDING` existant pour ce couple passe à `INVALIDATED` (même transaction).
- **FR-004/FR-005** : la validation MUST vérifier `status = PENDING` ET `expires_at > now()` avant de
  comparer le hash ; en cas de succès, `status` passe à `CONSUMED` (transition irréversible).

## Entité métier réutilisée (non modifiée)

### utilisateur (rappel, cf. `docs/merise/04-mld.md`)

Cette feature crée des lignes `utilisateur` de `type_compte = CLIENT` via la contrainte d'unicité
déjà présente en base (`UNIQUE (telephone, type_compte)`, `docs/merise/05-mpd.sql`) — qui applique
directement FR-007 (un numéro ne correspond jamais à plus d'un compte CLIENT) sans logique applicative
supplémentaire : une tentative de doublon échoue au niveau de la contrainte SQL.

## État et transitions

```text
otp_challenge.status :
  (création)  → PENDING
  PENDING     → CONSUMED     (validation réussie, FR-005)
  PENDING     → INVALIDATED  (nouveau challenge émis pour le même numéro, FR-006)
  PENDING      (expiré, expires_at < now(), non modifié en base mais traité comme invalide à la lecture — FR-005)
```

`CONSUMED` et `INVALIDATED` sont des états terminaux : aucune transition ne les fait revenir à `PENDING`.
