# Quickstart: Frontend partenaire — gestion établissement et RDV

Prérequis : `docker compose up -d --build api web`, un compte PARTENAIRE et son établissement
`VALIDE` existants.

## 1. Connexion (login OTP bloqué sans Zavu, testé via fixture comme 010)
1. `/partenaire/login` → demander le code, échec explicite affiché sans Zavu configuré.
2. Vérifier avec un code inséré en base (test) → tableau de bord accessible.

## 2. Tableau de bord et gestion
1. Le tableau de bord liste l'établissement du gérant (`GET /partenaire/etablissements`).
2. Ouvrir l'établissement → ajouter une prestation, la modifier, la supprimer.
3. Assigner une catégorie.

## 3. RDV
1. Onglet RDV → liste des demandes (`GET /partenaire/etablissements/{id}/rdv`).
2. Confirmer une demande "Demandé" → statut mis à jour.
3. Reprogrammer une demande confirmée sur un créneau occupé → message d'erreur explicite.

## 4. Erreurs
1. Couper temporairement `api`, recharger une page → message d'erreur explicite.
