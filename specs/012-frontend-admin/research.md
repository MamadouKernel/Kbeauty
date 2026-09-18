# Research: Frontend admin — KYC, modération et abonnements

## Décision 1 — Aucun ajout backend
**Décision**: contrairement à 010/011, aucun nouvel endpoint n'est nécessaire — `AdminController`
(004), `ModerationController` (009) et `BillingController` (008) couvrent déjà toutes les actions
requises par ce frontend.
**Rationale**: vérifié par lecture des trois contrôleurs avant de planifier cette feature.
**Alternatives rejetées**: aucune — pas de gap identifié.

## Décision 2 — Session admin par clé API, pas par identifiant utilisateur
**Décision**: `AdminSessionService` conserve directement la clé `X-Admin-Api-Key` en
`ProtectedLocalStorage` (pas un `idUtilisateur` comme pour client/gérant), envoyée telle quelle sur
chaque requête admin.
**Rationale**: cohérent avec le mécanisme réel côté API (`AdminApiKeyFilter` compare une clé statique,
pas d'entité "utilisateur admin" authentifiée) — reproduire un faux `idUtilisateur` serait trompeur.
**Alternatives rejetées**: simuler une session basée sur un compte admin (inexistant côté API pour
l'instant, dette technique documentée depuis 004).

## Décision 3 — Modération par identifiant saisi manuellement
**Décision**: la page de modération comporte un simple champ de saisie d'identifiant (établissement
ou utilisateur), pas une recherche/liste de comptes.
**Rationale**: aucun endpoint de recherche/liste de comptes utilisateurs n'existe côté API ; les
identifiants d'établissement restent accessibles depuis la liste des dossiers KYC. Construire un tel
endpoint serait un ajout backend hors du périmètre annoncé pour cette feature (YAGNI).
**Alternatives rejetées**: ajouter un endpoint de recherche de comptes — reporté à une itération
ultérieure si le besoin est confirmé.
