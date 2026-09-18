# Sprint 10 — Frontend partenaire, gestion établissement et RDV (Keke Beauty)

**Objectif** : deuxième frontend web (Blazor), parcours gérant partenaire — authentification,
tableau de bord des établissements gérés, gestion des prestations/catégories, traitement des
demandes de RDV.

## Sprint Backlog
| Story | Statut |
|---|---|
| Authentification gérant (OTP PARTENAIRE) | Fait (testé en réel : redirection login, contrat `VerifyOtp` typeCompte=1 validé via curl) |
| Découverte "mes établissements" | Fait (nouvel endpoint `GET /partenaire/etablissements`, testé en réel : liste correcte, `401` sans header) |
| Gestion prestations/catégories | Fait (ajout/modification/suppression/catégorie, testés en réel via curl contre les endpoints consommés par le frontend) |
| Liste et décisions RDV | Fait (nouvel endpoint `GET /partenaire/etablissements/{id}/rdv`, testé en réel : liste, `403` sur établissement non possédé) |

## Definition of Done
- [x] Redirection vers `/partenaire/login` si non authentifié, testée en réel dans le navigateur.
- [x] `GET /partenaire/etablissements` et `GET /partenaire/etablissements/{id}/rdv` testés en réel
  (curl) : succès, `401`, `403`.
- [x] Ajout de prestation testé en réel via l'endpoint consommé par la page (`PartnerManagementController`, inchangé).
- [x] `docker compose up -d --build api web` opérationnel avec les deux nouveaux endpoints.

## Dette technique explicite (confirmée)
`X-Partner-Id` en `ProtectedLocalStorage` (pas de JWT), même pattern que 010. Login UI complet
(du clic "Recevoir le code" jusqu'au tableau de bord) reste bloqué par Zavu non configuré, comme
pour toutes les fonctionnalités OTP du projet — contrat API vérifié en réel via curl à la place.

## Prochaine étape
Frontend admin (KYC, modération, tarifs abonnement) ; intégration du parcours abonnement/paiement
WiniPayer au frontend partenaire (différée, voir spec.md Assumptions de 011) ; raffinement visuel.
