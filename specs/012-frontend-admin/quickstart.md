# Quickstart: Frontend admin — KYC, modération et abonnements

Prérequis : `docker compose up -d --build api web`, un `ADMIN_API_KEY` valide, au moins un dossier
KYC `EN_ATTENTE` avec documents.

## 1. Connexion
1. `/admin/login`, saisir la clé → `200` attendu, accès au tableau de bord.
2. Saisir une clé invalide → message d'erreur explicite, pas d'accès.

## 2. Dossiers KYC
1. `/admin/dossiers?statut=EN_ATTENTE` → liste filtrée.
2. Ouvrir un dossier → détail (médias, infos).
3. Valider → statut `VALIDE` ; si documents manquants, message explicite (`409`).
4. Rejeter un autre dossier → statut `REJETE`.

## 3. Modération
1. `/admin/moderation`, saisir un id d'établissement (depuis la liste KYC) → suspendre → confirmation.
2. Réactiver → confirmation.
3. Id inexistant → message "introuvable".

## 4. Abonnements
1. `/admin/abonnements?statut=IMPAYE` → liste filtrée.
2. Déclencher une relance → résultat réel affiché (échec explicite sans Zavu, cohérent avec le reste du projet).
3. Modifier le tarif `ANNUEL` → nouvelle valeur affichée.
