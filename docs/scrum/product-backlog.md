# Product Backlog — Keke Beauty

Priorisation MoSCoW. Chaque story référencera une spec Spec Kit (`specs/<feature>/spec.md`) au moment
de son entrée en Sprint Backlog (Principe IV de la constitution).

## Epic 1 — Fondations techniques
| ID | User Story | Priorité | Feature Spec Kit |
|---|---|---|---|
| US-01 | En tant qu'équipe technique, je veux un environnement PostgreSQL reproductible via Docker Desktop | Must | `001-infra-postgres` |
| US-02 | En tant qu'équipe technique, je veux le schéma MPD (tables MERISE) appliqué en migration versionnée | Must | `001-infra-postgres` |

## Epic 2 — Identité & Comptes
| ID | User Story | Priorité | Feature Spec Kit |
|---|---|---|---|
| US-03 | En tant que client, je veux créer un compte par téléphone + OTP SMS | Must | `002-auth-client` |
| US-04 | En tant que partenaire, je veux m'inscrire avec upload photo devanture + pièce d'identité (KYC) | Must | `003-onboarding-partenaire` |
| US-05 | En tant qu'administrateur, je veux valider ou rejeter un dossier KYC | Must | `003-onboarding-partenaire` |

## Epic 3 — Annuaire & Recherche
| ID | User Story | Priorité | Feature Spec Kit |
|---|---|---|---|
| US-06 | En tant que client, je veux rechercher des établissements par catégorie et localisation | Must | `004-recherche-annuaire` |
| US-07 | En tant que client, je veux consulter la fiche établissement (médias, prestations, tarifs) | Must | `005-fiche-etablissement` |
| US-08 | En tant que client, je veux appeler l'établissement en un clic | Must | `005-fiche-etablissement` |
| US-09 | En tant que client, je veux un itinéraire vers Yango/Google Maps/Apple Maps | Must | `005-fiche-etablissement` |
| US-10 | En tant qu'administrateur, je veux gérer les catégories et zones géographiques | Should | `006-back-office-referentiels` |

## Epic 4 — Prise de Rendez-vous
| ID | User Story | Priorité | Feature Spec Kit |
|---|---|---|---|
| US-11 | En tant que partenaire, je veux gérer mes prestations et tarifs | Must | `007-gestion-prestations` |
| US-12 | En tant que client, je veux prendre RDV via un calendrier avec créneaux indisponibles en rouge | Must | `008-prise-rdv` |
| US-13 | En tant que partenaire, je veux valider/refuser/reprogrammer une demande de RDV | Must | `008-prise-rdv` |
| US-14 | En tant que client, je veux être notifié (SMS/in-app) de la confirmation ou du refus | Must | `008-prise-rdv` |

## Epic 5 — Abonnement & Paiement
| ID | User Story | Priorité | Feature Spec Kit |
|---|---|---|---|
| US-15 | En tant que partenaire, je veux souscrire un abonnement mensuel/annuel (engagement 1 an min) | Must | `009-abonnement` |
| US-16 | En tant que partenaire, je veux payer via Mobile Money ou carte bancaire | Must | `010-paiement` |
| US-17 | En tant qu'administrateur, je veux suivre les abonnements et relancer les impayés | Should | `011-back-office-finance` |
| US-18 | En tant qu'administrateur, je veux ajuster les tarifs d'abonnement | Should | `011-back-office-finance` |

## Epic 6 — Back-office & Modération
| ID | User Story | Priorité | Feature Spec Kit |
|---|---|---|---|
| US-19 | En tant qu'administrateur, je veux modérer (suspendre/réactiver) clients et établissements | Should | `006-back-office-referentiels` |

## Notes de priorisation Sprint 0
Ordre indicatif : US-01/US-02 (fondations) → US-03/US-04/US-05 (identité/KYC) →
US-06 à US-09 (annuaire) → US-11 à US-14 (RDV) → US-15/US-16 (paiement) → reste (back-office avancé).
