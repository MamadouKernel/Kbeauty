# PRD — Keke Beauty

> **Document de référence pour l'implémentation.**
> Fusion du Cahier des Charges Fonctionnel « Keke Beauty » et de l'analyse technique associée.
> Version 1.0 — 11/10/2026 — Auteur : Mamadou Konaté
>
> **Consigne pour l'agent de développement :** ce PRD fait foi. Les sections marquées **[HYPOTHÈSE]** sont des choix par défaut retenus pour lever les ambiguïtés du cahier des charges : implémente-les tels quels, mais rends-les paramétrables quand c'est indiqué et ne les « durcis » pas dans le code. Tout écart à ce document doit être signalé avant implémentation.

---

## Sommaire

1. Vision et objectifs
2. Périmètre (V1 / hors périmètre)
3. Personas et rôles
4. Stack technique imposée
5. Architecture
6. Charte graphique et UI
7. Exigences fonctionnelles
   - 7.1 Authentification (OTP)
   - 7.2 Annuaire et recherche (client)
   - 7.3 Fiche établissement (client)
   - 7.4 Prise de rendez-vous (client)
   - 7.5 Onboarding partenaire et KYC
   - 7.6 Gestion des prestations (partenaire)
   - 7.7 Agenda et RDV (partenaire)
   - 7.8 Abonnements et paiement
   - 7.9 Notifications
   - 7.10 Back-office administrateur
8. Modèle de données (PostgreSQL)
9. API REST (contrat indicatif)
10. Intégrations externes
11. Exigences non fonctionnelles
12. Plan de livraison par lots
13. Hypothèses retenues et questions ouvertes
14. Conventions de développement

---

## 1. Vision et objectifs

**Keke Beauty** est une plateforme qui sert d'**annuaire géolocalisé des établissements de beauté** (salons de coiffure, spas, salons de massage, ongleries, etc.), avec des modules de **mise en relation** et de **gestion de rendez-vous**.

| Cible | Besoin |
|---|---|
| **B2C : clients finaux** | Trouver rapidement un soin de beauté à proximité, voir les photos, les vidéos et les tarifs, appeler, s'y rendre et réserver. |
| **B2B : partenaires** | Gagner en visibilité, présenter leurs prestations et gérer leurs rendez-vous en ligne. |
| **Keke Beauty : admin** | Modérer, valider les partenaires (KYC) et monétiser via les abonnements. |

**Objectifs mesurables de la V1**
- Un client trouve un établissement par catégorie et par zone en moins de 3 interactions.
- Un partenaire complète son onboarding et son KYC en moins de 10 minutes.
- Une demande de RDV reçoit une réponse (confirmation ou refus) notifiée au client.
- Les encaissements d'abonnements se font en Mobile Money ou par carte via un agrégateur local.

---

## 2. Périmètre

### Dans le périmètre de la V1
- Application **web responsive, mobile-first, installable (PWA)** à 3 espaces : public/client, espace partenaire (`/pro`) et back-office (`/admin`).
- Authentification par téléphone + OTP SMS.
- Annuaire avec filtres par catégorie, par lieu (Pays > Région > Ville > Commune) et **par proximité**.
- Fiche établissement : médias, description, prestations, tarifs, bouton « Appeler », bouton « S'y rendre ».
- Prise de RDV avec calendrier dynamique (pour les établissements abonnés qui l'activent).
- Onboarding partenaire avec KYC et validation par l'admin.
- Gestion des prestations, des horaires, des indisponibilités et des RDV côté partenaire, en temps réel.
- Abonnements (plans paramétrables, mensuel ou annuel, engagement minimum de 12 mois) et paiement via un agrégateur (Mobile Money + cartes).
- Notifications SMS et in-app.
- Back-office : modération, KYC, suivi financier, paramétrage (tarifs, catégories, zones).

### Hors périmètre de la V1 (prévu pour la V2, à anticiper dans le modèle sans l'implémenter)
- Applications natives iOS et Android (prévoir un empaquetage **Capacitor** plus tard : ne pas utiliser d'API incompatibles).
- Avis et notes, favoris, rappel automatique avant RDV, push Firebase.
- Paiement ou acompte des RDV par le client **[HYPOTHÈSE : seuls les abonnements partenaires sont payants en V1]**.
- Messagerie client ↔ établissement.

---

## 3. Personas et rôles

| Rôle | Description | Accès |
|---|---|---|
| `Visitor` | Utilisateur non connecté | Parcours de l'annuaire et des fiches (lecture seule) **[HYPOTHÈSE : la consultation est publique, pour le référencement et l'acquisition ; le compte est requis pour « interagir », c'est-à-dire réserver]**. |
| `Client` | Utilisateur B2C inscrit | Réservation et historique des RDV, profil. |
| `PartnerOwner` | Gérant d'établissement | Onboarding, KYC, gestion d'un ou **plusieurs établissements** **[HYPOTHÈSE]**, abonnements. |
| `PartnerStaff` | Employé d'un établissement (V1 : modèle prêt, UI minimale) | Gestion de l'agenda de l'établissement. |
| `Admin` | Opérateur Keke Beauty | Modération, KYC, paramétrage, finances. |
| `SuperAdmin` | Administrateur technique | Gestion des admins, paramètres sensibles. |

Un même numéro de téléphone peut porter les rôles `Client` et `PartnerOwner`.

---

## 4. Stack technique imposée

| Couche | Technologie |
|---|---|
| Base de données | **PostgreSQL** (16+) avec les extensions **PostGIS**, `btree_gist`, `citext`, `unaccent`, `pg_trgm` |
| Backend | **C# / .NET 10** : ASP.NET Core Web API, **EF Core 10** + Npgsql + **NetTopologySuite** |
| Frontend | **Angular** (dernière version stable : composants standalone, **signals**, nouveau control flow `@if/@for`, détection de changement sans zone si supportée) + **PrimeNG** (version alignée sur Angular, thème « styled mode » avec preset personnalisé) |
| Temps réel | ASP.NET Core **SignalR** |
| Tâches planifiées | **Hangfire** (stockage PostgreSQL) |
| Stockage de fichiers | Stockage objet compatible S3 (**MinIO** en local ; S3, Cloudflare R2 ou équivalent en production) |
| Logs et observabilité | **Serilog** (JSON structuré) + OpenTelemetry + health checks |
| Tests | xUnit + FluentAssertions + **Testcontainers** (PostgreSQL/PostGIS) côté back ; runner de tests par défaut du CLI Angular côté front ; Playwright pour les tests E2E |
| Conteneurisation | Docker + `docker-compose` (api, web, postgres-postgis, minio, mailpit/sms-fake) |
| Hébergement cible | Cloud compatible conteneurs + PostgreSQL managé (Render, AWS…). **Firebase n'est pas retenu comme base de données** (incompatible avec le choix PostgreSQL). |

---

## 5. Architecture

### 5.1 Style
**Monolithe modulaire** : une seule API déployable, découpée en modules métier isolés (un namespace et un schéma par module, avec communication par interfaces et événements de domaine internes). Pas de microservices en V1.

### 5.2 Modules métier (bounded contexts)

| Module | Responsabilité |
|---|---|
| `Identity` | Comptes, OTP, JWT, refresh tokens, rôles |
| `Geography` | Référentiel Pays > Région > Ville > Commune |
| `Catalog` | Catégories de beauté, établissements, médias, prestations, recherche |
| `Onboarding` | Parcours partenaire, KYC, workflow de validation |
| `Scheduling` | Horaires, indisponibilités, ressources, calcul des créneaux, RDV |
| `Billing` | Plans, abonnements, factures, paiements, webhooks de l'agrégateur |
| `Notifications` | SMS, in-app, modèles de messages, journal d'envoi |
| `Administration` | Modération, tableaux de bord, paramètres globaux, journal d'audit |

### 5.3 Arborescence cible

```
keke-beauty/
├── backend/
│   ├── KekeBeauty.sln
│   ├── src/
│   │   ├── KekeBeauty.Api/                 # Hôte ASP.NET Core : endpoints, middlewares, SignalR hubs, DI
│   │   ├── KekeBeauty.SharedKernel/        # Result, erreurs, entités de base, événements de domaine, abstractions
│   │   ├── KekeBeauty.Infrastructure/      # EF Core DbContext, migrations, S3, SMS, paiement, Hangfire
│   │   └── Modules/
│   │       ├── KekeBeauty.Modules.Identity/
│   │       ├── KekeBeauty.Modules.Geography/
│   │       ├── KekeBeauty.Modules.Catalog/
│   │       ├── KekeBeauty.Modules.Onboarding/
│   │       ├── KekeBeauty.Modules.Scheduling/
│   │       ├── KekeBeauty.Modules.Billing/
│   │       ├── KekeBeauty.Modules.Notifications/
│   │       └── KekeBeauty.Modules.Administration/
│   └── tests/
│       ├── KekeBeauty.UnitTests/
│       └── KekeBeauty.IntegrationTests/    # Testcontainers PostGIS
├── frontend/
│   └── keke-beauty-web/                    # Angular workspace
│       └── src/app/
│           ├── core/        # auth, intercepteurs, guards, services API, SignalR
│           ├── shared/      # composants UI réutilisables, pipes
│           ├── theme/       # preset PrimeNG Keke Beauty
│           └── features/
│               ├── public/   # accueil, recherche, fiche, réservation
│               ├── account/  # profil client, mes RDV
│               ├── pro/      # espace partenaire (lazy)
│               └── admin/    # back-office (lazy)
├── docker-compose.yml
└── docs/  (ce PRD, ADR, schémas)
```

Chaque module contient les dossiers `Domain/` (entités, règles), `Application/` (cas d'usage, DTOs, validators FluentValidation) et `Endpoints/` (Minimal APIs groupées). Le code d'un module **n'accède jamais** directement aux tables d'un autre module : il passe par une interface publique du module concerné.

### 5.4 Choix transverses
- **Minimal APIs** regroupées par module (`MapGroup`), avec **OpenAPI** généré et un client TypeScript généré pour Angular (ex. `openapi-typescript` / NSwag).
- Validation avec **FluentValidation** ; erreurs au format **ProblemDetails** (RFC 9457).
- Dates stockées en `timestamptz` (UTC) ; fuseau de l'établissement stocké (`Africa/Abidjan` par défaut) pour le calcul des créneaux.
- Montants en `numeric(12,2)` + code devise ISO 4217 (`XOF` par défaut).
- Téléphones normalisés en **E.164** (libphonenumber-csharp).

---

## 6. Charte graphique et UI

### 6.1 Identité
- **Couleurs dominantes :** déclinaisons de **violet** (élégance, féminité, luxe) et de **blanc** (pureté, clarté, lisibilité).
- **Logo :** silhouette de visage féminin dans une goutte et une épingle de localisation, avec une fleur à gauche, en dégradé violet. Wordmark « KEKE BEAUTY » et baseline « SALONS • SPA • ONGLERIE ». Fichier à placer dans `frontend/keke-beauty-web/public/brand/logo.png` (fourni séparément).
- **Style :** design épuré, navigation intuitive centrée sur la **recherche par filtres** et la **mise en valeur du contenu visuel** (photos, vidéos courtes).

### 6.2 Tokens de couleur (approximations tirées du logo, à ajuster)

| Token | Valeur | Usage |
|---|---|---|
| `primary-50` | `#F6F0FB` | fonds légers |
| `primary-100` | `#EADCF6` | |
| `primary-200` | `#D5B8EC` | |
| `primary-300` | `#BC8FE0` | |
| `primary-400` | `#A366D2` | dégradé (clair) |
| `primary-500` | `#8B45C2` | **couleur principale** |
| `primary-600` | `#7433A8` | boutons (hover) |
| `primary-700` | `#5E2789` | |
| `primary-800` | `#4A1F6B` | wordmark, titres |
| `primary-900` | `#36174F` | |
| `surface-0` | `#FFFFFF` | fond principal |
| `danger` | rouge PrimeNG | **créneaux indisponibles** (exigence du CDC) |

Implémentation : un **preset PrimeNG** `KekeBeautyPreset` dérivé d'Aura via `definePreset`, avec la palette `primary` ci-dessus. Mode sombre facultatif en V1.

### 6.3 Principes UI
- Mobile-first, avec des zones tactiles d'au moins 44 px et une navigation basse sur mobile pour l'espace client.
- Cartes d'établissement **visuelles** : photo principale, nom, catégorie, commune et distance.
- Carrousel média (PrimeNG `Galleria` ou `Carousel`) acceptant images et vidéos courtes.
- Accessibilité **WCAG 2.1 AA** (contraste du violet sur blanc vérifié, labels, navigation au clavier).
- Interface **en français** ; préparer l'i18n (`@angular/localize` ou Transloco) sans traduire en V1.

---

## 7. Exigences fonctionnelles

> Format : **ID**, user story, puis critères d'acceptation (CA).

### 7.1 Authentification (OTP SMS)

**AUTH-01 : Inscription / connexion par téléphone**
En tant qu'utilisateur, je saisis mon numéro de téléphone, je reçois un code OTP par SMS et je suis connecté.
- CA1 : le numéro est validé et normalisé en E.164 ; le pays est choisi via un sélecteur (Côte d'Ivoire `+225` par défaut).
- CA2 : l'OTP comporte **6 chiffres**, est valable **5 minutes** et est stocké **haché** ; **5 tentatives** maximum, puis blocage de 15 minutes.
- CA3 : limitation de débit : au plus **3 envois par numéro et par 15 minutes**, et une limite par adresse IP (`RateLimiter` ASP.NET Core).
- CA4 : à la première connexion, on crée le compte (prénom, nom, e-mail facultatif) ; aux suivantes, on connecte simplement.
- CA5 : émission d'un **JWT d'accès** (15 min) et d'un **refresh token** (30 jours, rotatif, révocable, stocké haché).
- CA6 : en environnement de développement, un `FakeSmsSender` journalise l'OTP (aucun SMS réel envoyé).

**AUTH-02 : Comptes admin**
Les admins se connectent par **e-mail et mot de passe** (hachage ASP.NET Identity) avec **2FA TOTP obligatoire**. Pas d'inscription publique : seul un SuperAdmin peut créer un admin.

**AUTH-03 : Déconnexion et sessions**
Révocation du refresh token ; liste et révocation des sessions actives dans le profil.

### 7.2 Annuaire et recherche (client)

**SRCH-01 : Recherche par catégorie**
Je filtre par catégorie : Salon de coiffure, Espace spa, Massage, Onglerie, etc. (liste administrable, voir ADM-05).

**SRCH-02 : Recherche par localisation hiérarchique**
Je filtre par **Pays > Région > Ville > Commune** à l'aide de listes déroulantes en cascade.

**SRCH-03 : Recherche à proximité** *(complément du CDC)*
Avec l'accord de géolocalisation du navigateur, je vois les établissements triés par distance et filtrables par rayon (1, 3, 5, 10 ou 25 km).
- CA : requête PostGIS `ST_DWithin` / `ST_Distance` sur `geography(Point,4326)` avec un index GiST.

**SRCH-04 : Recherche textuelle**
Recherche par nom d'établissement ou de prestation (« tresses », « manucure »), insensible aux accents (`unaccent` + `pg_trgm`).

**SRCH-05 : Résultats**
- Affichage en liste de cartes, avec une **vue carte** en option (Google Maps ou Mapbox, voir §10).
- Filtres combinables ; pagination ou défilement infini.
- Filtre « Réservation en ligne disponible ».
- Seuls les établissements **actifs** (KYC validé, non suspendus) apparaissent.
- Tri par défaut : mis en avant (plan premium) d'abord, puis distance si connue, sinon pertinence.

### 7.3 Fiche établissement (client)

**EST-01 : Médias**
Carrousel de **photos et vidéos courtes** (ex. spécialité tresses).
- Photos : JPEG, PNG ou WebP, 10 Mo maximum, redimensionnées côté serveur (miniature et taille moyenne, au format WebP).
- Vidéos : MP4 (H.264), **60 s et 50 Mo maximum** **[HYPOTHÈSE]**, lecture native avec poster. Pas de transcodage en V1.

**EST-02 : Informations**
Description, catégorie(s), adresse et commune, **horaires d'ouverture**, **liste des prestations avec grille tarifaire** (prix fixe ou « à partir de », durée).

**EST-03 : Bouton « Appeler »**
Affiche le numéro du service client de l'établissement ; le bouton ouvre `tel:+225…`. Clic tracé (statistique partenaire).

**EST-04 : Bouton « S'y rendre »**
Ouvre une feuille de choix d'application d'itinéraire, avec les coordonnées GPS pré-remplies :
- **Yango** (deep link, voir §10.3)
- **Google Maps** : `https://www.google.com/maps/dir/?api=1&destination={lat},{lng}`
- **Apple Maps** (proposé sur iOS) : `https://maps.apple.com/?daddr={lat},{lng}`
Clic tracé.

**EST-05 : Bouton « Réserver »**
Visible uniquement si l'établissement a un abonnement donnant droit à la réservation **et** l'a activée.

**EST-06 : Partage**
Chaque fiche a une URL publique lisible (`/etablissements/{slug}`) avec métadonnées Open Graph.

### 7.4 Prise de rendez-vous (client)

**RDV-01 : Sélection**
Je choisis une ou plusieurs prestations, puis éventuellement un employé (« Sans préférence » par défaut), puis une date et un créneau.

**RDV-02 : Calendrier dynamique**
- Le calendrier affiche les jours ; les **dates ou heures indisponibles apparaissent en rouge** (exigence du CDC) et ne sont pas sélectionnables.
- Un créneau est calculé à partir des **horaires d'ouverture**, moins les **fermetures et indisponibilités**, moins les **RDV existants**, en tenant compte de la **durée totale des prestations choisies** et du **nombre de ressources** (employés ou postes) **[HYPOTHÈSE : le CDC ne précise pas ce point ; ce modèle est retenu]**.
- Pas de créneaux : 15 min (paramétrable par établissement). Délai minimum de réservation : 2 h ; horizon maximum : 60 jours (paramétrables).
- Les disponibilités se mettent à jour **en temps réel** (SignalR) : un créneau pris par quelqu'un d'autre passe en rouge sans rechargement.

**RDV-03 : Demande**
La validation crée un RDV au statut `Pending` (demande soumise à confirmation du partenaire). Le créneau est **bloqué** pendant l'attente.
- CA : **aucune double réservation possible**, garantie en base par une contrainte d'exclusion (voir §8.3).
- CA : une option par établissement permet la **confirmation automatique** (passage direct à `Confirmed`).
- CA : une demande sans réponse dans un délai de 24 h (paramétrable) **expire** (`Expired`), le créneau est libéré et le client est notifié.

**RDV-04 : Notification**
Le client reçoit un SMS et une notification in-app de **confirmation**, de **refus ou indisponibilité**, de **proposition de reprogrammation** ou d'**annulation**.

**RDV-05 : Mes RDV**
Liste « À venir » et « Passés » avec statut ; annulation par le client jusqu'à X heures avant (2 h par défaut, paramétrable par établissement).

**RDV-06 : Reprogrammation**
Quand le partenaire propose un nouveau créneau, le client **accepte** (le RDV est reconfirmé) ou **refuse** (le RDV est annulé) **[HYPOTHÈSE]**. Sans réponse avant le début du créneau proposé, le RDV est annulé.

**Machine à états du RDV**

```
Pending ──(partenaire valide | auto-confirm)──▶ Confirmed ──(fin de créneau)──▶ Completed
   │                                              │   │                         └─(partenaire marque)─▶ NoShow
   ├──(partenaire refuse)──▶ Rejected             │   └──(partenaire propose)──▶ RescheduleProposed
   ├──(délai dépassé)──▶ Expired                  │                                 ├─(client accepte)─▶ Confirmed
   └──(client annule)──▶ CancelledByClient        │                                 └─(client refuse / délai)─▶ CancelledByClient
                                                  ├──(client annule)──▶ CancelledByClient
                                                  └──(partenaire annule)──▶ CancelledByEstablishment
```

Chaque transition est historisée (`appointment_status_history`) et déclenche les notifications correspondantes.

### 7.5 Onboarding partenaire et KYC

**ONB-01 : Inscription partenaire**
Depuis « Inscrire mon établissement », je me connecte par OTP puis je suis un assistant (PrimeNG `Stepper`) :
1. **Informations de base** : nom de l'établissement, catégorie(s), description, téléphone du service client, e-mail (facultatif), réseaux sociaux (facultatif).
2. **Localisation** : Pays > Région > Ville > Commune, adresse textuelle et **position GPS exacte** (pointeur déplaçable sur la carte, pré-positionné par la géolocalisation du navigateur).
3. **Horaires** : horaires par jour de la semaine (plusieurs plages possibles par jour, ex. pause déjeuner).
4. **Photo de profil / devanture** (obligatoire).
5. **Pièce d'identité du gérant** : type (CNI, passeport, permis, carte consulaire), numéro, recto et verso, plus un selfie facultatif.
6. **Récapitulatif et soumission**.

- CA : brouillon sauvegardé à chaque étape (reprise possible).
- CA : à la soumission, statut `PendingReview` et notification à l'équipe admin.
- CA : l'établissement **n'est pas visible** dans l'annuaire tant que le KYC n'est pas `Approved`.

**ONB-02 : Statuts KYC**
`Draft` → `PendingReview` → `Approved` | `Rejected` (avec motif) | `ChangesRequested` (avec commentaire, le partenaire corrige et soumet à nouveau).
Le partenaire est notifié par SMS et in-app à chaque décision.

**ONB-03 : Sécurité des pièces d'identité**
- Stockées dans un **bucket privé distinct**, chiffré au repos, jamais exposé publiquement.
- Consultables par les seuls admins via des **URL pré-signées à durée courte (5 min)**.
- Chaque consultation est inscrite au **journal d'audit**.
- Purge des pièces des dossiers `Rejected` après 90 jours (paramétrable).

**ONB-04 : Multi-établissements**
Un gérant peut créer plusieurs établissements, chacun avec son propre KYC d'établissement. Le KYC de la personne (pièce d'identité) est **réutilisé** une fois approuvé.

### 7.6 Gestion des prestations (partenaire)

**SVC-01 :** Créer, modifier, supprimer (suppression logique) et réordonner des prestations : nom, catégorie, description, **prix** (fixe ou « à partir de »), **durée** (minutes), visibilité, et réservable en ligne ou non.
**SVC-02 :** Regrouper les prestations par rubrique (ex. « Tresses », « Soins visage »).
**SVC-03 :** Gérer la **galerie média** de l'établissement : ajout, suppression, ordre, choix de la photo de couverture, dans les limites du plan d'abonnement.

### 7.7 Agenda et RDV (partenaire)

**AGD-01 : Tableau de bord des RDV**
Demandes entrantes (`Pending`) en tête, avec actions **Valider**, **Refuser** (motif facultatif) et **Proposer un autre créneau**.
**AGD-02 : Vues calendrier** jour, semaine et mois des RDV confirmés, par ressource.
**AGD-03 : Annuler ou reprogrammer** un RDV confirmé, avec notification du client.
**AGD-04 : Indisponibilités** : fermetures exceptionnelles (congés, jours fériés), blocages ponctuels de créneaux, absences d'un employé.
**AGD-05 : Ressources** : gestion des employés ou postes et des prestations que chacun réalise. Par défaut, une ressource unique est créée automatiquement.
**AGD-06 : Temps réel** : les nouvelles demandes et annulations arrivent **en direct** (SignalR) avec une notification visuelle et sonore ; un badge compte les demandes en attente.
**AGD-07 : Paramètres de réservation** : activer ou désactiver la réservation, confirmation automatique, délai minimum, horizon, délai d'annulation et pas des créneaux.
**AGD-08 : Statistiques simples** : vues de la fiche, clics « Appeler » et « S'y rendre », nombre de RDV par statut sur la période.

### 7.8 Abonnements et paiement

**Modèle économique (CDC) :** les établissements souscrivent pour accéder aux **fonctionnalités avancées** (ex. module de réservation). **Engagement minimum : 1 an.** **Paiement mensuel ou annuel.** Les tarifs se modifient depuis le back-office.

**BIL-01 : Plans paramétrables** **[HYPOTHÈSE : contenu initial, entièrement modifiable par l'admin]**

| Plan | Contenu |
|---|---|
| **Gratuit** | Fiche dans l'annuaire, 5 photos, prestations et tarifs, « Appeler », « S'y rendre ». |
| **Premium** | Tout le plan Gratuit, **plus la réservation en ligne**, les vidéos, 30 médias, la mise en avant dans les résultats et les statistiques. |

Les fonctionnalités sont représentées par des **feature flags par plan** (`booking`, `videos`, `max_media`, `featured`, `stats`) et non codées en dur.

**BIL-02 : Souscription**
Le partenaire choisit le plan, puis la périodicité (**mensuelle**, avec 12 échéances d'engagement, ou **annuelle**, en un paiement éventuellement remisé), puis paie via l'agrégateur.
- CA : l'abonnement passe à `Active` **uniquement sur réception du webhook de paiement validé** (jamais sur le seul retour navigateur).
- CA : la date de fin d'engagement est **date de début + 12 mois**.

**BIL-03 : Prix figé**
Le prix et la périodicité sont **copiés dans l'abonnement** à la souscription ; une modification de tarif par l'admin ne s'applique qu'aux nouvelles souscriptions et aux renouvellements.

**BIL-04 : Échéances et relances (mensuel)**
Une facture est générée à chaque échéance (Hangfire), avec relance par SMS à J-3, J0, J+3 et J+7.
**BIL-05 : Impayés** **[HYPOTHÈSE]**
Après une période de grâce de 7 jours (paramétrable), l'abonnement passe à `PastDue` et les **fonctionnalités premium sont désactivées** : la réservation se ferme pour les nouvelles demandes et les RDV déjà confirmés sont conservés. La fiche reste visible en mode Gratuit. L'abonnement redevient `Active` dès régularisation.
**BIL-06 : Résiliation**
Une résiliation est possible, mais elle prend effet **à la fin de l'engagement** ; ensuite, renouvellement tacite par période ou fin, au choix du partenaire.
**BIL-07 : Factures**
Numérotation séquentielle sans trou (`KB-AAAA-000001`), avec un PDF téléchargeable (QuestPDF).
**BIL-08 : Moyens de paiement**
Mobile Money (**Wave, Orange Money, MTN, Moov**…) et **cartes bancaires (Visa, Mastercard)**, via un **agrégateur local** (CinetPay par défaut ; PaySika ou TouchPay en alternatives, voir §10.2).

**Statuts d'abonnement :** `PendingPayment` → `Active` → `PastDue` → `Active` | `Suspended` ; `Active` → `Cancelled` (en fin d'engagement) | `Expired`.

### 7.9 Notifications

**NOT-01 :** Canaux V1 : **SMS** et **in-app** (centre de notifications et toasts temps réel via SignalR). E-mail facultatif pour les admins. Push Firebase en V2.
**NOT-02 :** Modèles de messages éditables dans le back-office, avec variables (`{client_prenom}`, `{etablissement}`, `{date}`, `{heure}`…). SMS de 160 caractères maximum si possible.
**NOT-03 :** Événements notifiés : OTP ; demande de RDV reçue (partenaire) ; RDV confirmé, refusé, expiré, reprogrammé ou annulé (client) ; décision KYC (partenaire) ; paiement reçu ou échoué ; relances d'échéance ; nouvelle demande KYC (admin).
**NOT-04 :** Journal d'envoi (statut fournisseur, coût si disponible) et nouvelle tentative automatique (3 maximum, avec backoff).

### 7.10 Back-office administrateur

**ADM-01 : Gestion des utilisateurs** : recherche, fiche, **suspension et réactivation** des clients et partenaires, avec motif.
**ADM-02 : Modération des établissements** : suspendre ou masquer une fiche, retirer un média non conforme, avec historique.
**ADM-03 : Validation KYC** : file d'attente des dossiers `PendingReview` (les plus anciens d'abord) ; visionneuse des pièces (URL pré-signées) ; décision Approuver, Rejeter (motif obligatoire) ou Demander des corrections ; **contrôles semi-automatiques** affichés à titre indicatif (document expiré, nom du gérant différent du nom saisi, doublon de numéro de pièce).
**ADM-04 : Suivi financier** : tableau de bord avec abonnements actifs par plan, **revenu récurrent mensuel**, chiffre d'affaires encaissé par période et par moyen de paiement, liste des impayés (`PastDue`) avec **relance manuelle**, et export CSV.
**ADM-05 : Paramétrage** :
- **Plans et tarifs** (montants mensuel et annuel, feature flags)
- **Catégories de beauté** (ajout, icône, ordre, activation)
- **Zones géographiques** (ajout de pays, régions, villes et communes, avec activation)
- Paramètres globaux (délais OTP, grâce d'impayé, conservation KYC…)
**ADM-06 : Journal d'audit** : toute action admin et toute consultation de pièce d'identité est tracée (qui, quoi, quand, IP).
**ADM-07 : Gestion des admins** (SuperAdmin uniquement).

---

## 8. Modèle de données (PostgreSQL)

### 8.1 Conventions
- Noms en `snake_case` (convention de nommage EF Core `UseSnakeCaseNamingConvention`).
- Clés primaires en `uuid` (UUID v7 générés par l'application, `Guid.CreateVersion7()`).
- Colonnes d'audit `created_at`, `updated_at` et `deleted_at` (suppression logique avec filtre global EF) quand c'est pertinent.
- Un **schéma par module** : `identity`, `geography`, `catalog`, `onboarding`, `scheduling`, `billing`, `notifications`, `admin`.
- Concurrence optimiste via `xmin` (Npgsql) sur les agrégats sensibles.

### 8.2 Tables principales (colonnes clés)

**identity**
- `users` (id, phone_e164 *unique*, first_name, last_name, email citext null, status [Active|Suspended], created_at)
- `user_roles` (user_id, role)
- `otp_challenges` (id, phone_e164, code_hash, expires_at, attempts, consumed_at, ip)
- `refresh_tokens` (id, user_id, token_hash, expires_at, revoked_at, replaced_by, device_info)
- `admin_accounts` (id, user_id, email, password_hash, totp_secret_encrypted)

**geography**
- `countries` (id, iso2, name, phone_prefix, currency_code, is_active)
- `regions` (id, country_id, name, is_active)
- `cities` (id, region_id, name, is_active)
- `communes` (id, city_id, name, is_active, centroid geography(Point,4326) null)

**catalog**
- `beauty_categories` (id, name, slug, icon, sort_order, is_active)
- `establishments` (id, owner_user_id, name, slug *unique*, description, customer_service_phone, email, commune_id, address_line, **location geography(Point,4326)**, timezone, status [Draft|PendingReview|Active|Suspended|Rejected], is_featured, booking_enabled, auto_confirm, min_lead_minutes, max_horizon_days, cancel_limit_minutes, slot_step_minutes, created_at)
- `establishment_categories` (establishment_id, category_id)
- `media` (id, establishment_id, kind [Photo|Video], storage_key, thumb_key, poster_key, width, height, duration_sec, size_bytes, sort_order, is_cover, moderation_status)
- `service_groups` (id, establishment_id, name, sort_order)
- `services` (id, establishment_id, group_id null, category_id, name, description, price numeric(12,2), price_type [Fixed|From], currency_code, duration_minutes, is_bookable, is_visible, sort_order, deleted_at)
- `establishment_stats_daily` (establishment_id, day, views, call_clicks, directions_clicks)

**onboarding**
- `kyc_identities` (id, user_id, document_type, document_number, document_expiry, front_key, back_key, selfie_key, status, reviewed_by, reviewed_at, rejection_reason)
- `establishment_reviews` (id, establishment_id, status [PendingReview|Approved|Rejected|ChangesRequested], comment, reviewed_by, reviewed_at)

**scheduling**
- `opening_hours` (id, establishment_id, day_of_week, opens_at time, closes_at time)
- `resources` (id, establishment_id, name, is_active)
- `resource_services` (resource_id, service_id)
- `time_off` (id, establishment_id, resource_id null, period tstzrange, reason)
- `appointments` (id, establishment_id, client_user_id, resource_id, **period tstzrange**, status, total_price, currency_code, client_note, rejection_reason, proposed_period tstzrange null, expires_at, created_at)
- `appointment_services` (appointment_id, service_id, name_snapshot, price_snapshot, duration_snapshot)
- `appointment_status_history` (id, appointment_id, from_status, to_status, actor_user_id, at, comment)

**billing**
- `plans` (id, code, name, monthly_price, annual_price, currency_code, features jsonb, is_active, sort_order)
- `plan_price_history` (id, plan_id, monthly_price, annual_price, changed_by, changed_at)
- `subscriptions` (id, establishment_id, plan_id, billing_cycle [Monthly|Annual], unit_price_snapshot, currency_code, status, start_date, commitment_end_date, current_period_end, cancel_at_commitment_end bool, created_at)
- `invoices` (id, subscription_id, number *unique*, amount, currency_code, due_date, status [Open|Paid|Overdue|Void], pdf_key, paid_at)
- `payments` (id, invoice_id, provider, provider_transaction_id *unique*, method [Wave|OrangeMoney|MTN|Moov|Card|Other], amount, status [Pending|Succeeded|Failed], raw_payload jsonb, created_at)
- `payment_webhook_events` (id, provider, event_id *unique*, payload jsonb, received_at, processed_at) : **idempotence**

**notifications**
- `notification_templates` (id, code, channel, language, body)
- `notifications` (id, user_id, channel, template_code, payload jsonb, status, provider_message_id, error, sent_at, read_at)

**admin**
- `audit_logs` (id, actor_user_id, action, entity_type, entity_id, data jsonb, ip, at)
- `settings` (key, value jsonb, updated_by, updated_at)

### 8.3 Contraintes critiques

```sql
CREATE EXTENSION IF NOT EXISTS postgis;
CREATE EXTENSION IF NOT EXISTS btree_gist;

-- Aucune double réservation d'une même ressource sur des périodes qui se chevauchent
ALTER TABLE scheduling.appointments
  ADD CONSTRAINT no_overlapping_appointments
  EXCLUDE USING gist (resource_id WITH =, period WITH &&)
  WHERE (status IN ('Pending','Confirmed','RescheduleProposed'));

-- Recherche géographique
CREATE INDEX ix_establishments_location ON catalog.establishments USING gist (location);

-- Recherche textuelle insensible aux accents
CREATE INDEX ix_establishments_name_trgm ON catalog.establishments USING gin (name gin_trgm_ops);
```

La violation de la contrainte d'exclusion doit être interceptée et renvoyée en **409 Conflict** (« Ce créneau vient d'être réservé »), puis le calendrier du client est rafraîchi.

### 8.4 Données de démarrage (seed)
- Pays **Côte d'Ivoire** ; régions et villes principales ; **communes d'Abidjan** (Abobo, Adjamé, Attécoubé, Cocody, Koumassi, Marcory, Plateau, Port-Bouët, Treichville, Yopougon, Anyama, Bingerville, Songon).
- Catégories : Salon de coiffure, Espace spa, Massage, Onglerie, Barbier, Esthétique / Soins visage, Maquillage.
- Plans Gratuit et Premium (montants fictifs à remplacer par l'admin).
- Un compte SuperAdmin initial défini via des variables d'environnement.
- Jeu de démo (`--seed-demo`) : 10 établissements fictifs à Abidjan avec médias et prestations.

---

## 9. API REST (contrat indicatif)

Préfixe `/api/v1`. Authentification `Bearer` JWT. Réponses d'erreur au format ProblemDetails.

| Domaine | Endpoints |
|---|---|
| **Auth** | `POST /auth/otp/request` · `POST /auth/otp/verify` · `POST /auth/refresh` · `POST /auth/logout` · `GET /me` · `PATCH /me` · `POST /admin/auth/login` · `POST /admin/auth/2fa` |
| **Référentiels** | `GET /geo/countries` · `GET /geo/countries/{id}/regions` · `GET /geo/regions/{id}/cities` · `GET /geo/cities/{id}/communes` · `GET /categories` |
| **Recherche** | `GET /establishments?categoryId=&communeId=&cityId=&lat=&lng=&radiusKm=&q=&bookable=&page=&pageSize=&sort=` |
| **Fiche** | `GET /establishments/{slug}` · `POST /establishments/{id}/events` (`view`, `call`, `directions`) |
| **Disponibilités** | `GET /establishments/{id}/availability?from=&to=&serviceIds=&resourceId=` (jours et créneaux disponibles ou indisponibles) |
| **RDV client** | `POST /appointments` · `GET /me/appointments` · `POST /appointments/{id}/cancel` · `POST /appointments/{id}/reschedule/accept` · `POST /appointments/{id}/reschedule/decline` |
| **Partenaire** | `GET/POST /pro/establishments` · `GET/PUT /pro/establishments/{id}` · `PUT .../opening-hours` · `CRUD .../services` · `CRUD .../service-groups` · `POST .../media` (URL d'upload pré-signée) · `DELETE .../media/{mediaId}` · `CRUD .../resources` · `CRUD .../time-off` · `PUT .../booking-settings` · `POST .../submit` |
| **Agenda partenaire** | `GET /pro/establishments/{id}/appointments?status=&from=&to=` · `POST /pro/appointments/{id}/confirm` · `.../reject` · `.../propose-reschedule` · `.../cancel` · `.../no-show` |
| **KYC** | `POST /pro/kyc` · `GET /pro/kyc` |
| **Abonnement** | `GET /plans` · `GET /pro/establishments/{id}/subscription` · `POST /pro/establishments/{id}/subscription` (retourne l'URL de paiement) · `POST /pro/subscriptions/{id}/cancel` · `GET /pro/invoices` · `GET /pro/invoices/{id}/pdf` |
| **Webhooks** | `POST /webhooks/payments/{provider}` (anonyme, signature vérifiée, idempotent) |
| **Notifications** | `GET /me/notifications` · `POST /me/notifications/{id}/read` |
| **Admin** | `/admin/users` · `/admin/establishments` · `/admin/kyc/queue` · `/admin/kyc/{id}/decision` · `/admin/kyc/{id}/documents/{side}` (URL pré-signée + audit) · `/admin/plans` · `/admin/categories` · `/admin/geo/*` · `/admin/finance/dashboard` · `/admin/finance/past-due` · `/admin/subscriptions/{id}/remind` · `/admin/settings` · `/admin/audit-logs` · `/admin/notification-templates` |

**Hubs SignalR**
- `/hubs/availability` : groupe par `establishment:{id}`, événement `SlotsChanged(date)`.
- `/hubs/pro` : groupe par établissement, événements `AppointmentRequested`, `AppointmentCancelled` et `AppointmentUpdated`.
- `/hubs/notifications` : groupe par utilisateur, événement `NotificationReceived`.

---

## 10. Intégrations externes

Chaque intégration passe par une **interface** dans `SharedKernel` / `Infrastructure`, avec une implémentation **Fake** pour le développement et les tests, et une sélection du fournisseur par configuration (`appsettings` + variables d'environnement). **Aucun secret dans le dépôt.**

### 10.1 SMS : `ISmsSender`
- Implémentations : `InfobipSmsSender`, `TwilioSmsSender`, `FakeSmsSender`.
- Sender ID « KekeBeauty » si le pays et l'opérateur l'autorisent.

### 10.2 Paiement : `IPaymentGateway`
- Implémentation V1 : **CinetPay** (Mobile Money CI : Wave, Orange, MTN, Moov ; cartes Visa et Mastercard). Alternatives prévues : PaySika, TouchPay.
- Flux : création de la transaction, redirection vers le guichet de paiement, **webhook de notification**, **vérification côté serveur du statut auprès de l'API du fournisseur**, puis mise à jour de la facture et de l'abonnement.
- Exigences : vérification de signature ou du jeton du webhook, **idempotence** (`payment_webhook_events.event_id` unique), montant et devise recontrôlés, journalisation du payload brut.
- Tâche de réconciliation quotidienne pour les paiements `Pending` de plus de 30 minutes.

### 10.3 Cartographie et itinéraire
- `IMapProvider` côté front : **Google Maps JavaScript API** (par défaut) ou **Mapbox GL JS**, interchangeable par configuration. Usages : vue carte des résultats et sélecteur de position GPS de l'onboarding.
- Deep links vers les itinéraires, construits côté front par `DirectionsLinkService` :
  - Google Maps : `https://www.google.com/maps/dir/?api=1&destination={lat},{lng}`
  - Apple Maps : `https://maps.apple.com/?daddr={lat},{lng}`
  - **Yango** : deep link avec coordonnées de destination pré-remplies. **Le format exact est à confirmer dans la documentation officielle Yango** et doit être **configurable** (gabarit avec `{lat}` et `{lng}`), avec un repli sur le lien web ou le store si l'application n'est pas installée.

### 10.4 Stockage : `IFileStorage`
- Buckets : `public-media` (photos et vidéos des fiches, servies via CDN) et `private-kyc` (pièces d'identité, privé, chiffré).
- Upload direct depuis le navigateur via **URL pré-signée** ; validation serveur du type MIME réel, de la taille et de la durée vidéo après upload ; génération des miniatures (ImageSharp).

---

## 11. Exigences non fonctionnelles

| Domaine | Exigence |
|---|---|
| **Performance** | Recherche : p95 < 300 ms (hors réseau) jusqu'à 50 000 établissements. Calcul de disponibilités : p95 < 200 ms. Images servies en WebP via CDN avec chargement différé. |
| **Disponibilité et scalabilité** | API sans état (scalabilité horizontale), backplane SignalR prévu (Redis) si plusieurs instances, PostgreSQL managé avec sauvegardes quotidiennes et PITR. |
| **Sécurité** | OWASP ASVS niveau 2 visé. HTTPS partout, en-têtes de sécurité (CSP, HSTS), CORS restreint, rate limiting, validation de toutes les entrées, autorisation par politique **et** par propriété (un partenaire n'accède qu'à ses établissements), secrets en variables d'environnement ou coffre. |
| **Données personnelles** | Conformité à la **loi ivoirienne n° 2013-450** sur les données personnelles (autorité **ARTCI**) : finalités déclarées, consentement (CGU et politique de confidentialité), minimisation, durées de conservation, droit d'accès et de suppression (export et suppression du compte), journal des accès aux pièces d'identité. |
| **Observabilité** | Logs structurés Serilog avec corrélation par requête, traces OpenTelemetry, `/health/live` et `/health/ready`, tableau de bord Hangfire protégé (admin). |
| **Qualité** | Tests unitaires sur les règles métier (calcul de créneaux, machine à états des RDV, abonnements, impayés), tests d'intégration Testcontainers sur la contrainte anti-chevauchement et sur les webhooks, et E2E Playwright sur les parcours critiques (OTP → recherche → réservation ; onboarding → KYC → validation admin ; souscription → webhook → activation). |
| **Accessibilité** | WCAG 2.1 AA. |
| **Compatibilité** | 2 dernières versions de Chrome, Safari iOS, Samsung Internet, Firefox et Edge. Usable en 3G (bundle initial < 300 Ko gzip, routes chargées à la demande). |
| **PWA** | Manifest (icônes tirées du logo, `theme_color` violet), service worker pour le cache des ressources statiques, installable. |

---

## 12. Plan de livraison par lots

Chaque lot doit être **livrable, testé et démontrable** avant de passer au suivant.

| Lot | Contenu | Critère de fin |
|---|---|---|
| **0 : Socle** | Solution .NET modulaire, workspace Angular + PrimeNG + preset Keke Beauty, docker-compose (PostGIS, MinIO), EF Core et migrations, Serilog, OpenAPI → client TS, CI (build, tests, lint), seed des référentiels | `docker compose up` lance tout ; une page d'accueil aux couleurs Keke Beauty appelle `/health`. |
| **1 : Identité** | AUTH-01 à AUTH-03, gestion des rôles, guards Angular | Connexion OTP de bout en bout (Fake SMS). |
| **2 : Annuaire** | Catégories, géographie, SRCH-01 à 05, fiche EST-01 à 06 (sur données de démo) | Recherche par catégorie, zone et proximité ; fiche complète avec « Appeler » et « S'y rendre ». |
| **3 : Partenaire et KYC** | ONB-01 à 04, SVC-01 à 03, ADM-03 (file KYC) | Un partenaire s'inscrit, un admin valide, la fiche apparaît dans l'annuaire. |
| **4 : Rendez-vous** | Ressources, horaires, indisponibilités, calcul des créneaux, RDV-01 à 06, AGD-01 à 08, SignalR, notifications SMS et in-app | Réservation → validation partenaire → SMS client ; créneaux rouges en temps réel ; double réservation impossible. |
| **5 : Abonnements et paiement** | BIL-01 à 08, intégration CinetPay (bac à sable), feature flags appliqués (réservation conditionnée au plan) | Souscription payée en bac à sable → abonnement actif → réservation débloquée ; impayé → réservation fermée. |
| **6 : Back-office complet** | ADM-01, 02, 04 à 07, modèles de notifications, journal d'audit, exports | L'admin pilote les finances, les tarifs, les catégories et les zones. |
| **7 : Durcissement** | Sécurité, performance, accessibilité, PWA, conformité ARTCI, documentation de déploiement | Checklist de mise en production validée. |

---

## 13. Hypothèses retenues et questions ouvertes

Le cahier des charges initial laissait ces points ouverts. Les choix ci-dessous s'appliquent **par défaut** et doivent rester paramétrables. À valider avec le porteur du projet.

| # | Sujet | Hypothèse retenue |
|---|---|---|
| H1 | Mobile ou web | Web responsive + PWA en V1 ; empaquetage Capacitor (Android/iOS) en V2. |
| H2 | Accès sans compte | Consultation de l'annuaire publique ; compte requis pour réserver. |
| H3 | Calcul des créneaux | Durée par prestation + ressources (employés ou postes) ; une ressource unique par défaut. |
| H4 | Reprogrammation | Le partenaire propose, le client accepte ou refuse. |
| H5 | Gratuit / Premium | Fiche gratuite ; réservation, vidéos, mise en avant et statistiques en Premium (flags configurables). |
| H6 | Impayés pendant l'engagement | Grâce de 7 jours, puis fonctionnalités premium désactivées, fiche maintenue en Gratuit. |
| H7 | Paiement côté client | Aucun en V1 (pas d'acompte). |
| H8 | Multi-établissements | Un gérant peut posséder plusieurs établissements, avec un abonnement par établissement. |
| H9 | Multi-pays | Prévu dans le modèle (pays, devise, indicatif) ; Côte d'Ivoire et XOF seuls activés au lancement. |
| H10 | Vidéos | MP4 de 60 s et 50 Mo maximum, sans transcodage en V1. |
| H11 | Avis, favoris, rappels | Reportés en V2. |

**Questions ouvertes (non bloquantes pour les lots 0 à 3)**
- Q1 : Montants réels des plans (mensuel et annuel) et remise annuelle éventuelle.
- Q2 : Fournisseur SMS définitif (Infobip ou Twilio) et Sender ID.
- Q3 : Agrégateur de paiement définitif (CinetPay, PaySika ou TouchPay) et ouverture du compte marchand.
- Q4 : Format officiel du deep link Yango.
- Q5 : Faut-il un contrat ou des CGV signés électroniquement pour l'engagement d'un an ?
- Q6 : Choix final entre Google Maps et Mapbox (coût et quotas).

---

## 14. Conventions de développement

- **Langue** : code, noms de tables, API et commits en **anglais** ; interface utilisateur et messages en **français**.
- **Backend** : C# avec nullable activé, `TreatWarningsAsErrors`, analyzers .NET, `.editorconfig` ; pas de logique métier dans les endpoints ; cas d'usage testables sans HTTP.
- **Frontend** : composants standalone, signals pour l'état local, services `inject()`, formulaires typés (Reactive Forms), aucune logique métier dans les templates, ESLint + Prettier, `OnPush` partout.
- **Git** : Conventional Commits (`feat(scheduling): …`), une branche par fonctionnalité, PR avec description et tests.
- **Migrations EF** : une migration par changement de schéma, nommée explicitement, jamais modifiée après fusion.
- **Documentation** : décisions d'architecture consignées en ADR dans `docs/adr/` ; README avec démarrage local en moins de 5 commandes.
- **Définition de « terminé »** : critères d'acceptation couverts par des tests, build et lint au vert, migration appliquée, endpoint documenté dans OpenAPI, UI conforme à la charte.

---

*Fin du document.*
