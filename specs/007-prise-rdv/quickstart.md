# Phase 1 — Quickstart: Prise de Rendez-vous

## Scénario 1 — Créneaux occupés (US1)
```bash
curl "http://localhost:5080/etablissements/$ID/creneaux?date=2026-09-20"
```

## Scénario 2 — Demande de RDV (US2)
```bash
CLIENT_ID=<id_utilisateur_client>
curl -X POST -H "X-Client-Id: $CLIENT_ID" -H "Content-Type: application/json" \
  -d "{\"idEtablissement\":\"$ID\",\"idPrestation\":\"$PID\",\"dateHeureDebut\":\"2026-09-20T10:00:00Z\"}" \
  http://localhost:5080/rdv
```
Répéter sur le même créneau → `409` (chevauchement).

## Scénario 3 — Décisions du gérant (US3)
```bash
curl -X POST -H "X-Partner-Id: $PARTNER_ID" "http://localhost:5080/partenaire/etablissements/$ID/rdv/$RDV_ID/confirmer"
curl -X POST -H "X-Partner-Id: $PARTNER_ID" "http://localhost:5080/partenaire/etablissements/$ID/rdv/$RDV_ID/refuser"
curl -X POST -H "X-Partner-Id: $PARTNER_ID" -H "Content-Type: application/json" \
  -d '{"dateHeureDebut":"2026-09-20T14:00:00Z"}' \
  "http://localhost:5080/partenaire/etablissements/$ID/rdv/$RDV_ID/reprogrammer"
```

## Scénario 4 — Refus sur établissement non possédé
Attendu : `403` (même filtre que 006).
