# Parité V1 → V1.0.1 et feuille de route

Règle : **tout ce que la V1 en ligne (https://kekebeauty.duckdns.org) fait doit exister dans la V1.0.1**, puis on ajoute ce que la V1 n'a pas.
Source V1 : branche `main` (Blazor + API .NET 8 + Dapper). Source V1.0.1 : branche `v1.0.1` (Angular 21 + API .NET 10 modulaire).

Légende : ✅ présent · 🟡 partiel · ⬜ à faire · ➕ n'existe pas en V1 (apport V1.0.1)

## 1. Parité — fonctions de la V1

| # | Fonction V1 | V1.0.1 | Lot |
|---|-------------|--------|-----|
| 1 | Annuaire, recherche, filtres, carte | ✅ (+ carte plein écran, tri « mieux notés ») | — |
| 2 | Fiche établissement, prestations, horaires, galerie | ✅ (onglets, avis, vidéos) | — |
| 3 | Prise de RDV client + acompte en ligne | ✅ | — |
| 4 | Mes RDV, détail RDV, annulation/report | ✅ | — |
| 5 | Favoris | ✅ | — |
| 6 | Avis clients, réponse du partenaire | ✅ | — |
| 7 | Compte client (profil, notifications) | ✅ | — |
| 8 | Inscription / connexion e-mail + mot de passe, mot de passe oublié | ✅ | — |
| 9 | Double authentification, appareils de confiance | 🟡 (2FA oui, appareil de confiance non) | 6 |
| 10 | Connexion Google | ⬜ | 6 |
| 11 | Connexion par OTP téléphone / WhatsApp | ⬜ | 6 |
| 12 | Onboarding partenaire + KYC | ✅ | — |
| 13 | Abonnements partenaire (+ essai, changement de formule, promos) | ✅ ➕ | — |
| 14 | Agenda partenaire, confirmation/refus/report, no-show | ✅ | — |
| 15 | Prestations, catégories, horaires, équipe (ressources) | ✅ | — |
| 16 | Statistiques partenaire | ✅ | — |
| 17 | Admin : KYC, utilisateurs, établissements, abonnements, audit, paiements | ✅ | — |
| 18 | Admin : rôles fins (SUPER_ADMIN, KYC, SUPPORT, COMPTABLE) | 🟡 | 6 |
| 19 | PWA, notifications push | 🟡 (PWA oui, push web non) | 6 |
| 20 | **Comptoir** : RDV d'un client sans compte (walk-in) | ✅ | 2 |
| 21 | **Pass QR client** + **scanner QR partenaire** | ✅ | 2 |
| 22 | **Portail collaboratrice** : planning du jour | ✅ | 3 |
| 23 | **Poste de travail** : chrono début / fin de soin | ✅ | 3 |
| 24 | **Fiche technique cliente** (notes de soin, allergies) | ✅ | 3 |
| 25 | **Commissions collaboratrice** + pourboires | ✅ (calcul et suivi ; le versement reste à l'établissement) | 3 |
| 26 | **Pourboire** après un soin | 🟡 (enregistré ; pas encore encaissé en ligne) | 3 |
| 27 | Avis avec photos avant / après | ⬜ | 3 |
| 28 | **Keke Protect** (retard / annulation tardive, politique) | 🟡 (délai de grâce avant « absente » et versement suspendu pendant un litige ; pas de frais d'annulation tardive) | 4 |
| 29 | Litiges (ouverture client, traitement admin) | ✅ (+ réponse de l'établissement) | 4 |
| 30 | Remboursements (demande, validation admin) | ✅ (la demande passe par le litige ; l'admin valide, le remboursement part au moyen de paiement) | 4 |
| 31 | Reçus / justificatifs partenaire, finances détaillées | 🟡 | 5 |
| 32 | Tableau de bord admin analytique | 🟡 | 5 |
| 33 | Formules multiples, avantages, promotions d'un salon | ⬜ | 5 |
| 34 | Gestionnaire d'inactivité (déconnexion auto) | ⬜ | 6 |
| 35 | Paiement des collaboratrices (Wave) | ⬜ (gateway fictive d'abord) | 3b |

## 2. Apports V1.0.1 que la V1 n'a pas (à conserver et étendre)

| Apport | État |
|--------|------|
| Essai gratuit, changement de formule au prorata, codes promo, remise directe | ✅ |
| Carte plein écran avec pins informatifs, tuiles OSM mises en cache | ✅ |
| Vidéos de réalisations, mises à jour automatiques de l'appli | ✅ |
| Architecture modulaire testée (211 unitaires, 186 intégration, 172 front, 108 E2E) | ✅ |
| Lighthouse mobile 100 / 100 / 100 / 100 | ✅ |

## 3. Idées au-delà de la V1 (après la parité)

| Idée | Valeur | Lot |
|------|--------|-----|
| Liste d'attente automatique quand un créneau se libère | Remplit l'agenda | 7 |
| Forfaits / cartes de fidélité à points (au-delà du QR) | Rétention | 7 |
| Rappels RDV J-1 / H-2 et ré-engagement automatique à J+30 | Moins de no-show | 7 |
| Cartes cadeaux | Revenu additionnel | 7 |
| Réservation en groupe (mariage, anniversaire) | Panier moyen | 7 |
| Recommandations « salons proches de vous » | Découverte | 7 |
| Mode sombre et taille de texte réglable | Confort / accessibilité | 1 |
| Export comptable (CSV) pour le partenaire | Gain de temps | 5 |

## 4. Lots et ordre

1. **Design** : échelle typographique fluide et sobre, jetons d'espacement, tailles par écran.
2. **Comptoir + Pass QR + scanner** (partenaire).
3. **Équipe** : portail collaboratrice, chrono, fiche technique, commissions, pourboires ✅ ; reste (lot 3b) : avis avec photos avant / après, encaissement en ligne des pourboires, paiement Wave des collaboratrices.
4. **Confiance** : Keke Protect, litiges, remboursements ✅ ; reste : frais d'annulation tardive paramétrables par l'établissement.
5. **Finances & pilotage** : reçus, finances détaillées, tableau de bord admin, formules/avantages.
6. **Accès & sécurité** : Google, OTP WhatsApp, appareil de confiance, rôles admin fins, push web, inactivité.
7. **Au-delà de la V1** : liste d'attente, fidélité, cartes cadeaux, groupes, recommandations.

Chaque lot : backend + front + tests (unitaires, intégration, E2E) + mise à jour de `ETAT-DES-FONCTIONNALITES.md` + commit.
