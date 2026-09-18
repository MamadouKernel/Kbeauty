# Sprint 8 — Modération back-office (Keke Beauty)

**Objectif** : un administrateur suspend/réactive un compte ou un établissement ; les fonctionnalités
principales (OTP, annuaire, RDV, abonnement) refusent explicitement toute action sur une cible
suspendue.

## Sprint Backlog
| Story | Statut |
|---|---|
| US-19 — Suspension/réactivation clients et établissements | Fait (testé en réel : suspension/réactivation, 404, idempotence, effets sur OTP/annuaire/RDV/abonnement) |

## Definition of Done
- [x] Suspension/réactivation idempotente, `404` sur identifiant inexistant (testé en réel).
- [x] Compte client suspendu : OTP refusé (`403`, testé en réel).
- [x] Établissement suspendu : absent de la recherche/fiche annuaire (testé en réel).
- [x] Établissement suspendu : RDV et souscription d'abonnement refusés (testés en réel).
- [x] Réactivation restaure exactement l'état antérieur (`statut_kyc` inchangé, vérifié en réel).

## Dette technique explicite (confirmée)
`Admin:ApiKey` — pas de RBAC réel (même dette que 004/005/006/007/008). Aucune notification envoyée
au client/gérant lors d'une suspension (hors périmètre CDC pour cette feature).

## Prochaine étape
Product Backlog complet (Epics 1 à 6, US-01 à US-19). Reste la configuration différée de
Zavu/CinetPay pour les tests de bout en bout réels des fonctionnalités déjà implémentées.
