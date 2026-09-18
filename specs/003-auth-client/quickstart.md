# Phase 1 — Quickstart: Inscription et Authentification Client par Téléphone

## Prérequis
- Features `001-infra-postgres` et `002-scaffold-dotnet` opérationnelles.
- Migration `0003_otp_challenge.sql` appliquée (`./scripts/db/migrate.sh`).

## Scénario 1 — Première inscription (US1)

```bash
curl -s -X POST http://localhost:5080/auth/otp/request \
  -H "Content-Type: application/json" \
  -d '{"telephone":"+2250700000001"}'
```

**Résultat attendu (sans configuration Zavu)** : `502` avec `status: "send_failed"` — comportement
explicite et attendu tant que le projet Zavu Keke Beauty n'est pas configuré (cf. spec, Assumptions).
**Une fois Zavu configuré** : `202` avec `status: "sent"`, puis récupérer le code reçu sur WhatsApp et :

```bash
curl -s -X POST http://localhost:5080/auth/otp/verify \
  -H "Content-Type: application/json" \
  -d '{"telephone":"+2250700000001","code":"<code reçu>"}'
```

**Résultat attendu** : `200` avec `status: "verified"`, `isNewAccount: true`.

## Scénario 2 — Reconnexion (US2)

Répéter le Scénario 1 avec le même numéro : `isNewAccount` doit être `false` sur le `verify`, et
`SELECT count(*) FROM utilisateur WHERE telephone = '+2250700000001' AND type_compte = 'CLIENT'`
doit rester égal à `1`.

## Scénario 3 — Code invalide, expiré ou déjà utilisé (US3)

```bash
# Code volontairement incorrect
curl -s -X POST http://localhost:5080/auth/otp/verify \
  -H "Content-Type: application/json" \
  -d '{"telephone":"+2250700000001","code":"000000"}'
```

**Résultat attendu** : `400` avec `status: "invalid_or_expired_code"`.

Réutiliser un code déjà validé avec succès (Scénario 1) doit produire la même réponse `400`.

## Scénario 4 — Nouvelle demande invalide l'ancienne (Edge Case / FR-006)

Demander deux codes consécutifs pour le même numéro sans valider le premier, puis tenter de valider
le premier code : la réponse attendue est `400` (invalidé par la seconde demande).

## Vérification base de données (sans dépendre de Zavu)

```bash
docker exec kekebeauty-postgres psql -U kekebeauty -d kekebeautyDb -c \
  "SELECT status, count(*) FROM otp_challenge GROUP BY status;"
```
