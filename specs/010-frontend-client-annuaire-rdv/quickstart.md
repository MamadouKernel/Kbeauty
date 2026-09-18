# Quickstart: Frontend client — annuaire et prise de RDV

Prérequis : `docker compose up -d --build api web`, un établissement `VALIDE` avec catégorie et
prestation existants.

## 1. Recherche et fiche établissement
1. Ouvrir `http://localhost:${WEB_PORT}/`.
2. Filtrer par catégorie/commune → la liste se met à jour.
3. Ouvrir une fiche → médias/prestations/tarifs visibles, lien `tel:` et lien itinéraire présents.

## 2. Authentification OTP
1. Aller sur `/login`, saisir un numéro E.164.
2. Sans Zavu configuré : message d'échec explicite affiché (pas d'écran vide).
3. Avec un code connu en base (test), le vérifier → session locale posée (`idUtilisateur`).

## 3. Prise de RDV
1. Depuis une fiche établissement, choisir une date → créneaux occupés affichés en indisponible.
2. Choisir un créneau libre + une prestation → soumettre.
3. Statut "Demandé" affiché immédiatement (réponse de `POST /rdv`).
4. Simuler une décision gérant via curl (`POST /partenaire/.../confirmer`), recharger la page RDV →
   statut mis à jour via `GET /rdv/{id}`.

## 4. Erreurs
1. Couper temporairement le conteneur `api`, recharger une page → message d'erreur explicite,
   jamais une page blanche.
