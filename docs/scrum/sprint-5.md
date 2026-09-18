# Sprint 5 — Gestion des Prestations et Catégories par le Partenaire (Keke Beauty)

**Objectif** : le gérant gère lui-même ses prestations/catégories (OTP généralisé au type PARTENAIRE),
remplaçant le mécanisme admin temporaire (005) pour l'usage courant.

## Sprint Backlog
| Story | Statut |
|---|---|
| US-11 — OTP généralisé PARTENAIRE | Fait (testé : add/update/delete prestation, assign/remove catégorie) |
| US-11 — CRUD prestations propriétaire uniquement | Fait (`403` confirmé sur établissement non possédé) |

## Definition of Done
- [x] Ajout prestation → `201`, modification → `200`, suppression → `200` (testé en réel).
- [x] Tentative sur établissement non possédé → `403` explicite (testé en réel).
- [x] Assignation/retrait catégorie → effet immédiat vérifié en recherche (005, testé en réel).

## Dette technique explicite (confirmée)
En-tête `X-Partner-Id` comme preuve d'identité — pas de session/JWT. À remplacer par une vraie
gestion de session dans une feature future d'authentification.

## Prochaine étape
Epic 4 — prise de RDV (US-12 à US-14), qui dépend de la recherche (005) et des prestations (006)
pour identifier l'établissement et la prestation ciblés.
