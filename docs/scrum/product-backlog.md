# Product Backlog — Keke Beauty

Priorisation MoSCoW. Chaque story référencera une spec Spec Kit (`specs/<feature>/spec.md`) au moment
de son entrée en Sprint Backlog (Principe IV de la constitution).

## Epic 1 — Fondations techniques
| ID | User Story | Priorité | Feature Spec Kit |
|---|---|---|---|
| US-01 | En tant qu'équipe technique, je veux un environnement PostgreSQL reproductible via Docker Desktop | Must | `001-infra-postgres` |
| US-02 | En tant qu'équipe technique, je veux le schéma MPD (tables MERISE) appliqué en migration versionnée | Must | `001-infra-postgres` |
| US-02b | En tant qu'équipe technique, je veux un squelette applicatif backend .NET démarrable en une commande, connecté à la base, avec vérification de santé | Must | `002-scaffold-dotnet` |

## Epic 2 — Identité & Comptes
| ID | User Story | Priorité | Feature Spec Kit |
|---|---|---|---|
| US-03 | En tant que client, je veux créer un compte par téléphone + OTP WhatsApp (Zavu) | Must | `003-auth-client` |
| US-04 | En tant que partenaire, je veux m'inscrire avec upload photo devanture + pièce d'identité (KYC) | Must | `004-onboarding-partenaire` |
| US-05 | En tant qu'administrateur, je veux valider ou rejeter un dossier KYC | Must | `004-onboarding-partenaire` |

## Epic 3 — Annuaire & Recherche
| ID | User Story | Priorité | Feature Spec Kit |
|---|---|---|---|
| US-06 | En tant que client, je veux rechercher des établissements par catégorie et localisation | Must | `005-recherche-annuaire` |
| US-07 | En tant que client, je veux consulter la fiche établissement (médias, prestations, tarifs) | Must | `005-recherche-annuaire` |
| US-08 | En tant que client, je veux appeler l'établissement en un clic | Must | `005-recherche-annuaire` |
| US-09 | En tant que client, je veux un itinéraire vers Yango/Google Maps/Apple Maps | Must | `005-recherche-annuaire`, complété `018-extensions-completes` (3 options réelles au lieu d'un seul lien Google Maps) |
| US-33 | En tant que client, je veux ajouter un établissement à mes favoris | Could | `018-extensions-completes` |
| US-10 | En tant qu'administrateur, je veux gérer les catégories et zones géographiques | Should | `006-back-office-referentiels` |

## Epic 4 — Prise de Rendez-vous
| ID | User Story | Priorité | Feature Spec Kit |
|---|---|---|---|
| US-11 | En tant que partenaire, je veux gérer mes prestations et tarifs | Must | `006-gestion-prestations` |
| US-12 | En tant que client, je veux prendre RDV via un calendrier avec créneaux indisponibles en rouge | Must | `007-prise-rdv` |
| US-13 | En tant que partenaire, je veux valider/refuser/reprogrammer une demande de RDV | Must | `007-prise-rdv` |
| US-14 | En tant que client, je veux être notifié (SMS/in-app) de la confirmation ou du refus | Must | `007-prise-rdv`, canal in-app complété `018-extensions-completes` (canal SMS/WhatsApp reste dépendant de Zavu) |

## Epic 5 — Abonnement & Paiement
| ID | User Story | Priorité | Feature Spec Kit |
|---|---|---|---|
| US-15 | En tant que partenaire, je veux souscrire un abonnement mensuel/annuel (engagement 1 an min) | Must | `008-abonnement-paiement` |
| US-16 | En tant que partenaire, je veux payer via Mobile Money ou carte bancaire | Must | `008-abonnement-paiement` |
| US-17 | En tant qu'administrateur, je veux suivre les abonnements et relancer les impayés | Should | `008-abonnement-paiement` |
| US-18 | En tant qu'administrateur, je veux ajuster les tarifs d'abonnement | Should | `008-abonnement-paiement` |

## Epic 6 — Back-office & Modération
| ID | User Story | Priorité | Feature Spec Kit |
|---|---|---|---|
| US-19 | En tant qu'administrateur, je veux modérer (suspendre/réactiver) clients et établissements | Should | `009-moderation-back-office` |

## Epic 7 — Historique & Paiement en ligne RDV
| ID | User Story | Priorité | Feature Spec Kit |
|---|---|---|---|
| US-20 | En tant que client, je veux consulter l'historique de tous mes rendez-vous (à venir et passés) avec leur statut | Must | `013-historique-rdv-paiement` |
| US-21 | En tant que client, je veux pouvoir payer en ligne (Mobile Money) au moment de la demande de RDV, en plus du paiement sur place | Should | `013-historique-rdv-paiement` |
| US-22 | En tant que client, je veux pouvoir vérifier manuellement le statut d'un paiement resté en attente | Could | `013-historique-rdv-paiement` |

## Epic 8 — Avis clients
| ID | User Story | Priorité | Feature Spec Kit |
|---|---|---|---|
| US-23 | En tant que client, je veux laisser un avis (note + commentaire) après un rendez-vous terminé | Must | `014-avis-clients` |
| US-24 | En tant que visiteur, je veux consulter la note moyenne et les avis réels d'un salon | Must | `014-avis-clients` |

## Epic 9 — Résilience & Kéké Protect
| ID | User Story | Priorité | Feature Spec Kit |
|---|---|---|---|
| US-25 | En tant que client, je veux relancer un paiement en ligne échoué sans perdre mon créneau | Must | `015-annulation-remboursement` |
| US-26 | En tant que client, je veux annuler un RDV à venir, avec suivi de remboursement si déjà payé | Must | `015-annulation-remboursement` |

## Epic 10 — Équipe & Analytics
| ID | User Story | Priorité | Feature Spec Kit |
|---|---|---|---|
| US-27 | En tant que partenaire, je veux référencer les collaboratrices de mon salon | Should | `016-gestion-equipe` |
| US-28 | En tant qu'administrateur, je veux un tableau de bord global temps réel de la plateforme | Must | `017-admin-analytics` |

## Epic 11 — Extensions à 100% (pourboire, portail collaboratrice, litiges)
| ID | User Story | Priorité | Feature Spec Kit |
|---|---|---|---|
| US-29 | En tant que client, je veux laisser un pourboire directement attribué à la praticienne (0% commission) | Could | `018-extensions-completes` |
| US-30 | En tant que collaboratrice, je veux me connecter (OTP) pour consulter mon planning individuel, tenir les fiches techniques de mes clientes et suivre mes pourboires | Should | `018-extensions-completes` |
| US-31 | En tant que client ou gérant, je veux signaler un litige sur un RDV ; en tant qu'administrateur, je veux l'instruire et le résoudre | Should | `018-extensions-completes` |
| US-32 | En tant qu'administrateur, je veux une file d'attente des demandes de remboursement et pouvoir les clôturer avec une référence de virement tracée | Should | `018-extensions-completes` |

Hors périmètre, constat honnête documenté (voir `specs/018-extensions-completes/spec.md`, Assumptions) :
l'intégration d'une API de remboursement/payout automatique reste hors de portée — WiniPayer
n'expose qu'un checkout marchand (création + vérification), aucune API de virement P2P. Le
**processus** de remboursement (demande → file d'attente admin → traitement tracé) est en place
depuis US-32 ; seul le virement lui-même reste manuel. Même limitation structurelle pour le
transfert final du pourboire à la collaboratrice (US-29) : le paiement est capté sur le compte
marchand Keke Beauty (aucune retenue de commission), le reversement effectif à la collaboratrice
reste manuel/off-plateforme.

## Notes de priorisation Sprint 0
Ordre indicatif : US-01/US-02 (fondations) → US-03/US-04/US-05 (identité/KYC) →
US-06 à US-09 (annuaire) → US-11 à US-14 (RDV) → US-15/US-16 (paiement) → reste (back-office avancé).
