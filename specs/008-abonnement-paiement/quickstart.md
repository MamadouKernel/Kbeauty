# Quickstart: Abonnement et Paiement

Prérequis : `docker compose up -d --build api`, un `X-Partner-Id` valide (gérant propriétaire d'un
établissement existant), un `Admin:ApiKey` valide.

## 1. Souscription (WiniPayer TEST configuré → lien de paiement réel généré)
```bash
curl -s -X POST http://localhost:$API_PORT/etablissements/$ID_ETAB/abonnements \
  -H "X-Partner-Id: $ID_PARTNER" -H "Content-Type: application/json" \
  -d '{"periodicite":"MENSUEL"}'
```
Attendu : `201`, `{ "idAbonnement", "statut": "IMPAYE", "checkoutUrl": "https://checkout.winipayer.com/..." }`.

## 1bis. Simuler le callback WiniPayer (paiement confirmé)
Le callback réel arrive après paiement sur `checkoutUrl`. Pour tester sans payer réellement,
calculer le hash attendu et l'envoyer :
```bash
UUID="<reference_externe recuperee en base ou dans les logs>"
PRIVATE_KEY="$WINIPAYER_TEST_PRIVATE_KEY"
HASH=$(printf "%s%s%s%s%s" "$PRIVATE_KEY" "$UUID" "$CRYPTO" "$AMOUNT" "$CREATED_AT" | sha256sum | cut -d' ' -f1)
curl -s -X POST http://localhost:$API_PORT/webhooks/winipayer/callback -H "Content-Type: application/json" \
  -d "{\"uuid\":\"$UUID\",\"crypto\":\"$CRYPTO\",\"amount\":$AMOUNT,\"created_at\":\"$CREATED_AT\",\"state\":\"success\",\"operator\":\"wave-cote-divoire\",\"hash\":\"$HASH\"}"
```
Attendu : `{ "status": "applied" }`, puis l'abonnement passe `ACTIF` et la transaction `REUSSIE`. Un
rejeu du même callback renvoie `{ "status": "already_processed_or_unknown" }` (idempotent).

## 2. Double souscription (FR-004)
Répéter l'appel ci-dessus avec un abonnement déjà `ACTIF` → `409 Conflict`.

## 3. Établissement non possédé (FR-003)
Même appel avec un `X-Partner-Id` d'un autre gérant → `403 Forbidden`.

## 4. Liste admin par statut
```bash
curl -s "http://localhost:$API_PORT/api/admin/abonnements?statut=IMPAYE" -H "Admin:ApiKey: $ADMIN_KEY"
```

## 5. Relance
```bash
curl -s -X POST http://localhost:$API_PORT/api/admin/abonnements/$ID_ABO/relance -H "Admin:ApiKey: $ADMIN_KEY"
```
Attendu : `notificationEnvoyee: false` sans Zavu configuré (cohérent avec 003/006/007).

## 6. Tarif standard
```bash
curl -s "http://localhost:$API_PORT/api/admin/tarifs" -H "Admin:ApiKey: $ADMIN_KEY"
curl -s -X PUT "http://localhost:$API_PORT/api/admin/tarifs/ANNUEL" -H "Admin:ApiKey: $ADMIN_KEY" \
  -H "Content-Type: application/json" -d '{"montant": 50000}'
```
Vérifier qu'une nouvelle souscription `ANNUEL` utilise `50000`, et qu'un abonnement `ANNUEL`
déjà existant garde son ancien montant.
