# Phase 1 — Quickstart: Recherche et Consultation de l'Annuaire par le Client

## Prérequis
- Feature `004-onboarding-partenaire` opérationnelle, avec au moins un établissement `VALIDE`
  (ex. celui créé/validé lors des tests de la feature 004).

## Scénario 1 — Assigner catégorie et prestation (US4, FR-008)

```bash
ID=<id-etablissement-valide>
curl -s -X POST -H "X-Admin-Api-Key: $ADMIN_API_KEY" -H "Content-Type: application/json" \
  -d '{"libelleCategorie":"Spa"}' "http://localhost:5080/admin/applications/$ID/categories"
curl -s -X POST -H "X-Admin-Api-Key: $ADMIN_API_KEY" -H "Content-Type: application/json" \
  -d '{"libellePrestation":"Massage relaxant","tarif":15000,"dureeMinutes":60}' \
  "http://localhost:5080/admin/applications/$ID/prestations"
```

## Scénario 2 — Recherche par catégorie (US1)

```bash
curl -s "http://localhost:5080/etablissements?categorie=Spa"
```

**Résultat attendu** : l'établissement apparaît dans la liste.

```bash
curl -s "http://localhost:5080/etablissements?categorie=Onglerie"
```

**Résultat attendu** : liste vide (`[]`), pas d'erreur (FR-003).

## Scénario 3 — Exclusion d'un établissement non validé (US2)

Créer une nouvelle soumission partenaire (feature 004, restée `EN_ATTENTE`), lui assigner une
catégorie via le Scénario 1, puis :

```bash
curl -s "http://localhost:5080/etablissements?categorie=Spa"
```

**Résultat attendu** : seul l'établissement déjà `VALIDE` apparaît, pas celui `EN_ATTENTE`.

## Scénario 4 — Consultation de la fiche (US3)

```bash
curl -s "http://localhost:5080/etablissements/$ID"
```

**Résultat attendu** : nom, description, `numeroServiceClient`, `lienItineraire` (URL Google Maps),
`medias`, `prestations` (contenant "Massage relaxant").

## Scénario 5 — Fiche d'un établissement non validé ou inexistant (US3, FR-007)

```bash
curl -s -w "\nHTTP_STATUS:%{http_code}\n" "http://localhost:5080/etablissements/<id-en-attente>"
curl -s -w "\nHTTP_STATUS:%{http_code}\n" "http://localhost:5080/etablissements/00000000-0000-0000-0000-000000000099"
```

**Résultat attendu** : `404` identique dans les deux cas.
