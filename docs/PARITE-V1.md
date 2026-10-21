# Parité V1 → V1.0.1 et feuille de route

Règle : **tout ce que la V1 en ligne (https://kekebeauty.duckdns.org) fait doit exister dans la V1.0.1**, puis on ajoute ce que la V1 n'a pas.
Source V1 : branche `main` (Blazor + API .NET 8 + Dapper). Source V1.0.1 : branche `v1.0.1` (Angular 21 + API .NET 10 modulaire).

Légende : ✅ présent · 🟡 partiel · ⬜ à faire · ➕ n'existe pas en V1 (apport V1.0.1)

## 1. Parité — fonctions de la V1

| # | Fonction V1 | V1.0.1 | Lot |
|---|-------------|--------|-----|
| 1 | Annuaire, recherche, filtres, carte | ✅ (+ carte plein écran, tri « mieux notés ») | — |
| 2 | Fiche établissement, prestations, horaires, galerie | ✅ (onglets, avis, vidéos) | — |
| 3 | Prise de RDV client + acompte en ligne | ✅ (développé ; paiement en ligne du rendez-vous mis hors service par décision, voir `ETAT-DES-FONCTIONNALITES.md`) | — |
| 4 | Mes RDV, détail RDV, annulation/report | ✅ | — |
| 5 | Favoris | ✅ | — |
| 6 | Avis clients, réponse du partenaire | ✅ | — |
| 7 | Compte client (profil, notifications) | ✅ | — |
| 8 | Inscription / connexion e-mail + mot de passe, mot de passe oublié | ✅ | — |
| 9 | Double authentification, appareils de confiance | ✅ (TOTP Google Authenticator + codes de secours + appareil de confiance 30 jours révocable depuis « Mon compte ») | 6 |
| 10 | Connexion Google | ✅ (vérification du jeton Google Identity Services + liaison au compte + 2FA / appareil de confiance) | 6 |
| 11 | Connexion par OTP téléphone / WhatsApp | ✅ (demande et vérification de code OTP WhatsApp sur la page de connexion quand le fournisseur est activé + liaison depuis « Mon compte ») | 6 |
| 12 | Onboarding partenaire + KYC | ✅ | — |
| 13 | Abonnements partenaire (+ essai, changement de formule, promos) | ✅ ➕ | — |
| 14 | Agenda partenaire, confirmation/refus/report, no-show | ✅ | — |
| 15 | Prestations, catégories, horaires, équipe (ressources) | ✅ | — |
| 16 | Statistiques partenaire | ✅ | — |
| 17 | Admin : KYC, utilisateurs, établissements, abonnements, audit, paiements | ✅ | — |
| 18 | Admin : rôles fins (SUPER_ADMIN, KYC, SUPPORT, COMPTABLE) | ✅ (`SuperAdmin`, `Admin`, `AdminKyc`, `AdminSupport`, `AdminAccountant` avec politiques d'autorisation par périmètre et attribution/modification dans `/admin/administrateurs`) | 6 |
| 19 | PWA, notifications push | ✅ (manifeste PWA + service worker + notifications Web Push VAPID RFC 8291/8188 + gestion par appareil dans « Mon compte ») | 6 |
| 20 | **Comptoir** : RDV d'un client sans compte (walk-in) | ✅ | 2 |
| 21 | **Pass QR client** + **scanner QR partenaire** | ✅ | 2 |
| 22 | **Portail collaboratrice** : planning du jour | ✅ | 3 |
| 23 | **Poste de travail** : chrono début / fin de soin | ✅ | 3 |
| 24 | **Fiche technique cliente** (notes de soin, allergies) | ✅ | 3 |
| 25 | **Commissions collaboratrice** + pourboires | ✅ (commission figée à la fin du soin ; versement par mobile money) | 3 |
| 26 | **Pourboire** après un soin | ✅ (payé en ligne, versé à la collaboratrice) | 3 |
| 27 | Avis avec photos avant / après | ✅ (4 photos au plus, masquées avec l'avis) | 3 |
| 28 | **Keke Protect** (retard / annulation tardive, politique) | ✅ (délai de grâce avant « absente », versement suspendu pendant un litige, frais d'annulation tardive paramétrables) | 4 |
| 29 | Litiges (ouverture client, traitement admin) | ✅ (+ réponse de l'établissement) | 4 |
| 30 | Remboursements (demande, validation admin) | ✅ (la demande passe par le litige ; l'admin valide, le remboursement part au moyen de paiement) | 4 |
| 31 | Reçus / justificatifs partenaire, finances détaillées | ✅ (synthèse financière par prestation et mode de paiement, reçu PDF de prestation et bordereau PDF de reversement) | 5 |
| 32 | Tableau de bord admin analytique | ✅ (KPI salons par statut, RDV par statut et volume honoré, MRR et abonnements, litiges, communauté et avis) | 5 |
| 33 | Formules multiples, avantages, promotions d'un salon | ✅ (plafond de prestations et avantages personnalisés par plan + promotions et éléments inclus par prestation/formule d'un salon) | 5 |
| 34 | Gestionnaire d'inactivité (déconnexion auto) | ✅ (bannière d'avertissement 60 s avant expiration avec bouton « Rester connecté(e) » + déconnexion automatique côté client et serveur) | 6 |
| 35 | Paiement des collaboratrices (Wave) | ✅ (Wave, Orange, MTN, Moov ; passerelle fictive en test, WiniPayer en production) | 3 |

## 2. Apports V1.0.1 que la V1 n'a pas (à conserver et étendre)

| Apport | État |
|--------|------|
| Essai gratuit, changement de formule au prorata, codes promo, remise directe | ✅ |
| Carte plein écran avec pins informatifs, tuiles OSM mises en cache | ✅ |
| Vidéos de réalisations, mises à jour automatiques de l'appli | ✅ |
| Architecture modulaire testée (223 unitaires, 197 intégration, 192 front, 115 E2E) | ✅ |
| Lighthouse mobile 100 / 100 / 100 / 100 | ✅ |

## 3. Idées au-delà de la V1 (après la parité)

| Idée | Valeur | Lot |
|------|--------|-----|
| Liste d'attente automatique quand un créneau se libère ✅ | Remplit l'agenda | 7 |
| Forfaits / cartes de fidélité à points (au-delà du QR) ✅ | Rétention | 7 |
| Rappels RDV J-1 / H-2 et ré-engagement automatique à J+30 ✅ | Moins de no-show | 7 |
| Cartes cadeaux ✅ | Revenu additionnel | 7 |
| Réservation en groupe (mariage, anniversaire) ✅ | Panier moyen | 7 |
| Recommandations « salons proches de vous » ✅ | Découverte | 7 |
| Mode sombre et taille de texte réglable ✅ | Confort / accessibilité | 7 |
| Export comptable (CSV) pour le partenaire ✅ | Gain de temps | 5 |

## 4. Lots et ordre

1. **Design** : échelle typographique fluide et sobre, jetons d'espacement, tailles par écran ✅.
2. **Comptoir + Pass QR + scanner** (partenaire) ✅.
3. **Équipe** : portail collaboratrice, chrono, fiche technique, commissions, pourboires, avis avec photos, paiement des collaboratrices ✅.
4. **Confiance** : Keke Protect, litiges, remboursements, frais d'annulation tardive ✅.
5. **Finances & pilotage** : reçus, finances détaillées, export comptable CSV, tableau de bord admin, formules/avantages et promotions salon ✅.
6. **Accès & sécurité** : Google, OTP WhatsApp, appareil de confiance, rôles admin fins, push web, inactivité ✅.
7. **Au-delà de la V1** : liste d'attente, fidélité, cartes cadeaux, groupes, recommandations, ré-engagement J+30, mode sombre et taille de texte ✅.

Chaque lot : backend + front + tests (unitaires, intégration, E2E) + mise à jour de `ETAT-DES-FONCTIONNALITES.md` + commit.

## 5. Vérification ligne à ligne contre le code (21 octobre 2026)

Méthode : les routes et pages de la V1 (`git show main:…`, contrôleurs et pages Blazor) ont été comparées aux routes de l'API et aux pages Angular de la V1.0.1. Le tableau du §1 reste exact ; la vérification de terrain relève **trois écarts réels** que ce tableau ne mentionnait pas.

| Fonction V1 | Route / page V1 | V1.0.1 | Écart |
|---|---|---|---|
| Recherche d'adresse (géocodage) et adresse depuis un point de carte | `GET geo/search`, `GET geo/reverse` | ✅ `GET /api/v1/geo/search` et `/reverse`, boutons dans l'assistant d'établissement | Fait le 21 octobre 2026 (API → OpenStreetMap, une requête par seconde, cache 24 h, 30/min par utilisateur) |
| Le client signale un retard | `POST rdv/{id}/retard` (minutes) | ✅ `POST /api/v1/appointments/{id}/late` (10, 15 ou 30 min, une fois, de 3 h avant à 30 min après le début) | Fait le 21 octobre 2026 : boutons dans « Mes rendez-vous », notification au salon |
| Le client demande lui-même un report | `POST rdv/{id}/reprogrammer` | ✅ `POST /api/v1/appointments/{id}/reschedule` : créneau libre des mêmes prestations, même délai qu'une annulation, 2 déplacements au plus | Fait le 21 octobre 2026 : « Déplacer le rendez-vous » dans « Mes rendez-vous », le salon est prévenu |
| Fiche technique | `rdv/{id}/fiche-technique` | ✅ remplacée par une note par cliente (`clients/{id}/note`), plus utile : elle survit aux rendez-vous | — |
| Assignation d'un RDV à une collaboratrice | `assigner` | ✅ équipe et ressources (`team/{resourceId}`) | — |
| Auto-clôture des RDV | job de complétion | ✅ `AppointmentMaintenance` / `MaintenanceWorker` | — |
| Export, modération, journal, litiges, remboursements, abonnements admin | `admin/*` | ✅ | — |
| Pages Blazor `/prototype`, `/mot-de-passe` (connexion par mot de passe) | — | ✅ intégrées à la connexion | — |

## 6. Propositions pour faire mieux que la V1 (par priorité)

1. ✅ **Report demandé par le client** (fait).
2. ✅ **« Je suis en retard »** en un bouton (fait ; la prolongation automatique du délai de grâce reste possible).
3. ✅ **Aide à la saisie d'adresse** (fait).
4. **Changer son mot de passe connecté(e)** (absent de la V1 comme de la V1.0.1) avec déconnexion des autres appareils.
5. **Passkeys / WebAuthn** pour partenaires et admins (écart ASVS V2).
6. **Vérification des mots de passe fuités** (k-anonymat), **rotation des clés** documentée et testée.
7. **Rappel par WhatsApp/SMS** et avis post-soin automatiques (dépend de LeTexto).

Chaque ajout suivra la règle du dépôt : tests (unitaires, intégration, E2E), puis passe de sécurité consignée dans `docs/PENTEST.md`.
