# Phase 1 — Quickstart: Gestion des Prestations et Catégories par le Partenaire

## Scénario 1 — OTP partenaire (US1)
```bash
curl -X POST http://localhost:5080/auth/otp/request -H "Content-Type: application/json" \
  -d '{"telephone":"+2250700000002","typeCompte":"Partenaire"}'
```
Sans Zavu configuré : `502` explicite (cohérent avec 003). Récupérer `idUtilisateur` du gérant via
`SELECT id_utilisateur FROM utilisateur WHERE telephone='+2250700000002' AND type_compte='PARTENAIRE';`

## Scénario 2 — Gestion prestations (US2)
```bash
PARTNER_ID=<id_utilisateur_gerant>
ID=<id_etablissement_du_gerant>
curl -X POST -H "X-Partner-Id: $PARTNER_ID" -H "Content-Type: application/json" \
  -d '{"libellePrestation":"Coupe","tarif":5000,"dureeMinutes":30}' \
  "http://localhost:5080/partenaire/etablissements/$ID/prestations"
```
Puis `PUT .../prestations/{idPrestation}` et `DELETE .../prestations/{idPrestation}`.

## Scénario 3 — Refus sur établissement non possédé (FR-004)
```bash
curl -s -w "\nHTTP_STATUS:%{http_code}\n" -X POST -H "X-Partner-Id: $PARTNER_ID" \
  -H "Content-Type: application/json" -d '{"libellePrestation":"X","tarif":1,"dureeMinutes":1}' \
  "http://localhost:5080/partenaire/etablissements/<autre-id>/prestations"
```
Attendu : `403`.

## Scénario 4 — Catégories (US3)
```bash
curl -X POST -H "X-Partner-Id: $PARTNER_ID" -H "Content-Type: application/json" \
  -d '{"libelleCategorie":"Onglerie"}' "http://localhost:5080/partenaire/etablissements/$ID/categories"
curl -X DELETE -H "X-Partner-Id: $PARTNER_ID" "http://localhost:5080/partenaire/etablissements/$ID/categories/<idCategorie>"
```
