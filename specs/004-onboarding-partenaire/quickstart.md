# Phase 1 — Quickstart: Inscription et Validation KYC des Établissements Partenaires

## Prérequis
- Features `001`, `002`, `003` opérationnelles.
- Migration `0004_geo_seed_minimal.sql` appliquée.
- `.env` contient `ADMIN_API_KEY` (valeur de test locale, ex. `dev-admin-key`).

## Scénario 1 — Soumission d'un dossier (US1)

```bash
curl -s -w "\nHTTP_STATUS:%{http_code}\n" -X POST http://localhost:5080/partners/applications \
  -F "telephone=+2250700000002" \
  -F "nomEtablissement=Salon Test" \
  -F "gpsLatitude=5.359952" \
  -F "gpsLongitude=-4.008256" \
  -F "numeroServiceClient=+2250700000003" \
  -F 'horaires={"lundi":"9h-18h"}' \
  -F "photoDevanture=@./devanture.jpg" \
  -F "pieceIdentite=@./piece.jpg"
```

**Résultat attendu** : `201` avec `statut: "EN_ATTENTE"`.

## Scénario 2 — Soumission incomplète (Edge Case / FR-003)

Répéter sans `pieceIdentite` : `400 invalid_submission`.

## Scénario 3 — Consultation et validation admin (US2)

```bash
curl -s -H "X-Admin-Api-Key: $ADMIN_API_KEY" http://localhost:5080/admin/applications?statut=EN_ATTENTE
curl -s -H "X-Admin-Api-Key: $ADMIN_API_KEY" http://localhost:5080/admin/applications/<id>
curl -s -H "X-Admin-Api-Key: $ADMIN_API_KEY" -o piece.jpg http://localhost:5080/admin/applications/<id>/files/piece-identite
curl -s -X POST -H "X-Admin-Api-Key: $ADMIN_API_KEY" http://localhost:5080/admin/applications/<id>/validate
```

**Résultat attendu** : liste contient le dossier, fichier téléchargé, `validate` → `200 {"statut":"VALIDE"}`.

## Scénario 4 — Rejet et notification (US2/FR-009)

```bash
curl -s -X POST -H "X-Admin-Api-Key: $ADMIN_API_KEY" http://localhost:5080/admin/applications/<id>/reject
```

**Résultat attendu** : `200 {"statut":"REJETE"}`. Notification WhatsApp au gérant : `502`/échec explicite
tant que Zavu n'est pas configuré (comportement cohérent avec la feature 003).

## Scénario 5 — Accès admin sans clé (Sécurité / Décision 4)

```bash
curl -s -w "\nHTTP_STATUS:%{http_code}\n" http://localhost:5080/admin/applications
```

**Résultat attendu** : `401`.

## Scénario 6 — Invisibilité tant que non validé (US3)

Vérifier en base que `SELECT statut_kyc FROM etablissement WHERE id_etablissement = '<id>'` reflète
l'état courant ; la feature de recherche client (à venir) devra filtrer sur `statut_kyc = 'VALIDE'`.
