# Sprint 7 — Abonnement et Paiement (Keke Beauty)

**Objectif** : un gérant propriétaire souscrit un abonnement et le paie ; un administrateur consulte
les abonnements, relance les impayés, et ajuste le tarif standard.

## Sprint Backlog
| Story | Statut |
|---|---|
| US-15 — Souscription abonnement mensuel/annuel | Fait (testé : succès/échec paiement, engagement via `date_debut_engagement`) |
| US-16 — Paiement Mobile Money/carte | Fait (échec explicite sans agrégateur configuré, cohérent avec Zavu) |
| US-17 — Suivi et relance des impayés par l'admin | Fait (liste par statut, relance testées) |
| US-18 — Ajustement du tarif d'abonnement | Fait (modification testée, non rétroactive) |

## Definition of Done
- [x] Souscription reflète fidèlement succès/échec de paiement (testé en réel).
- [x] Un seul abonnement `ACTIF` par établissement, vérifié atomiquement (testé : `409` sur doublon).
- [x] `403` sur souscription pour un établissement non possédé (testé en réel).
- [x] Liste admin par statut, relance (échec explicite sans Zavu), tarifs lecture/écriture testés en réel.
- [x] Modification de tarif non rétroactive (vérifié : abonnement existant garde son montant).

## Dette technique explicite (confirmée)
`X-Partner-Id`/`Admin:ApiKey` — pas de session/JWT ni RBAC réel (même dette que 004/006/007).
Agrégateur de paiement (CinetPay) non configuré : comportement testé uniquement sur le chemin
d'échec explicite, comme Zavu.

## Prochaine étape
Product Backlog Epic 5 complet. Reste Epic 6 (US-19, modération back-office) et la configuration
différée de Zavu/CinetPay pour les tests de bout en bout réels.
