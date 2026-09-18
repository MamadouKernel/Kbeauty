# Quickstart: Modération back-office

Prérequis : `docker compose up -d --build api`, `./scripts/db/migrate.sh`, un `Admin:ApiKey` valide,
un compte client et un établissement `VALIDE` existants.

## 1. Suspendre un compte client, puis vérifier le refus d'OTP (FR-005)
```bash
curl -s -X POST "http://localhost:$API_PORT/admin/utilisateurs/$ID_CLIENT/suspendre" -H "X-Admin-Api-Key: $ADMIN_KEY"
curl -s -o /dev/null -w "%{http_code}\n" -X POST "http://localhost:$API_PORT/auth/otp/request" \
  -H "Content-Type: application/json" -d "{\"telephone\":\"$TELEPHONE\"}"
```
Attendu : `403`.

## 2. Réactiver, vérifier le retour à la normale
```bash
curl -s -X POST "http://localhost:$API_PORT/admin/utilisateurs/$ID_CLIENT/reactiver" -H "X-Admin-Api-Key: $ADMIN_KEY"
```

## 3. Suspendre un établissement, vérifier sa disparition de l'annuaire (FR-006)
```bash
curl -s -X POST "http://localhost:$API_PORT/admin/etablissements/$ID_ETAB/suspendre" -H "X-Admin-Api-Key: $ADMIN_KEY"
curl -s "http://localhost:$API_PORT/etablissements" | grep -c "$ID_ETAB"
```
Attendu : `0` occurrence.

## 4. Refus de RDV et d'abonnement sur établissement suspendu (FR-007/FR-008)
```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST "http://localhost:$API_PORT/rdv" \
  -H "X-Client-Id: $ID_CLIENT" -H "Content-Type: application/json" \
  -d "{\"idEtablissement\":\"$ID_ETAB\",\"idPrestation\":\"$ID_PRESTATION\",\"dateHeureDebut\":\"2026-09-25T10:00:00Z\"}"

curl -s -o /dev/null -w "%{http_code}\n" -X POST "http://localhost:$API_PORT/etablissements/$ID_ETAB/abonnements" \
  -H "X-Partner-Id: $ID_PARTNER" -H "Content-Type: application/json" -d '{"periodicite":"MENSUEL","canal":"WAVE"}'
```

## 5. Idempotence et 404 (FR-009/FR-004)
```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST "http://localhost:$API_PORT/admin/etablissements/$ID_ETAB/suspendre" -H "X-Admin-Api-Key: $ADMIN_KEY"
curl -s -o /dev/null -w "%{http_code}\n" -X POST "http://localhost:$API_PORT/admin/etablissements/00000000-0000-0000-0000-000000000000/suspendre" -H "X-Admin-Api-Key: $ADMIN_KEY"
```
Attendu : `200` (répété), `404` (inexistant).

## 6. Réactiver l'établissement, vérifier son retour en annuaire
