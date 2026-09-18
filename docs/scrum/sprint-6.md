# Sprint 6 — Prise de Rendez-vous (Keke Beauty)

**Objectif** : le client demande un RDV sur un créneau libre ; le gérant valide/refuse/reprogramme ;
le client est notifié.

## Sprint Backlog
| Story | Statut |
|---|---|
| US-12 — Créneaux occupés + demande | Fait (testé : créneau libre → 201, chevauchement → 409) |
| US-13 — Décisions gérant | Fait (confirmer/refuser/reprogrammer testés, `403` sur non-possédé) |
| US-14 — Notification client | Fait (échec explicite sans Zavu, cohérent avec 003/004) |

## Definition of Done
- [x] Créneaux occupés reflètent les RDV `DEMANDE`/`CONFIRME` (testé en réel).
- [x] Chevauchement détecté de façon atomique, 0 double réservation possible (testé en réel).
- [x] Décisions gérant fonctionnelles, `403` sur établissement non possédé (testé en réel).
- [x] Notification tentée à chaque décision (testé en réel, échec explicite documenté).

## Dette technique explicite (confirmée)
`X-Client-Id`/`X-Partner-Id` — pas de session/JWT. RG-RDV-01 (module RDV conditionné à un abonnement
actif) non appliquée : différée à la future feature abonnement/paiement (Epic 5).

## Prochaine étape
Epic 5 — abonnement et paiement (US-15 à US-18), seule partie majeure du Product Backlog restante.
