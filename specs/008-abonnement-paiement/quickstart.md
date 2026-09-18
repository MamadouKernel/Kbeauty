# Quickstart: Abonnement et Paiement

Prérequis : `docker compose up -d --build api`, un `X-Partner-Id` valide (gérant propriétaire d'un
établissement existant), un `Admin:ApiKey` valide.

## 1. Souscription réussie (agrégateur non configuré → échec explicite attendu par défaut)
```bash
curl -s -X POST http://localhost:$API_PORT/api/etablissements/$ID_ETAB/abonnements \
  -H "X-Partner-Id: $ID_PARTNER" -H "Content-Type: application/json" \
  -d '{"periodicite":"MENSUEL","canal":"WAVE"}'
```
Attendu (sans `Billing:CinetPay:ApiKey`) : `201`, `statut: IMPAYE`, `statutTransaction: ECHOUEE`.

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
