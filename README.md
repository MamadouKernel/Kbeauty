# Keke Beauty

Annuaire géolocalisé des salons de coiffure, spas, massages et ongleries, avec réservation en ligne.
Spécification de référence : [PRD_Keke_Beauty.md](PRD_Keke_Beauty.md). Design : [design-system/keke-beauty/MASTER.md](design-system/keke-beauty/MASTER.md).

**Avancement : lots 0 à 4** (socle, identité, annuaire, partenaire et KYC, rendez-vous) — voir [specs](specs).

## Démarrage rapide (Docker)

Sous Windows, double-cliquez sur **`start.cmd`** (ou lancez-le dans un terminal) : il démarre Docker Compose, donc **la base
PostgreSQL/PostGIS en premier**, puis le stockage, l'API et le site, et attend que l'API réponde. `start.cmd build` reconstruit
d'abord l'API et le site après des changements de code ; `stop.cmd` arrête tout **sans effacer les données**. Les services
redémarrent d'eux-mêmes avec Docker (`restart: unless-stopped`).

```bash
docker compose up --build   # équivalent, sur tout système
```

**Première fois sur une machine : générer les secrets.** Le dépôt est public, donc il ne contient aucun mot de passe : exécutez une fois
`sh scripts/init-env.sh` (Git Bash ou WSL sous Windows). Il crée `.env` à partir de `.env.example` et remplit avec des valeurs aléatoires le
mot de passe PostgreSQL, les identifiants MinIO et la clé de signature des jetons ; `docker compose` refuse de démarrer tant qu'ils sont vides.
`.env` est ignoré par git : ne le commitez jamais. Les ports de la pile ne sont publiés que sur `127.0.0.1` ; ne l'exposez pas
(tunnel, réseau partagé, serveur) : c'est une pile de développement.

| Service | URL |
|---|---|
| Web | http://localhost:4200 |
| API (OpenAPI) | http://localhost:8080/openapi/v1.json |
| Santé de l'API | http://localhost:8080/health/ready |
| Console MinIO | http://localhost:9001 |
| PostgreSQL/PostGIS | `localhost:5434` (base `kekebeauty`, utilisateur `keke`) |

Les migrations et le seed (Côte d'Ivoire, communes d'Abidjan, 7 catégories) s'appliquent au démarrage de l'API.

## Authentification (lot 1)

Connexion par **e-mail + mot de passe**, avec **Google Authenticator (TOTP) obligatoire pour tous** ; connexion Google
prête mais **désactivée** (WhatsApp : désactivé, non implémenté).

- **Premier compte :** `/inscription` → l'e-mail de confirmation est *journalisé* en développement :
  `docker compose logs api | findstr confirmation-email` (aucun SMTP réel avant le lot des notifications).
- **Première connexion :** scanner le QR code avec Google Authenticator, saisir le code, **conserver les 10 codes de récupération**.
- **SuperAdmin initial (optionnel) :** définir `ADMIN_EMAIL` et `ADMIN_PASSWORD` dans `.env` (voir `.env.example`).
- **Connexion Google :** créer un client OAuth « Application Web » (origine `http://localhost:4200`), puis
  `GOOGLE_AUTH_ENABLED=true` et `GOOGLE_CLIENT_ID=...` dans `.env`.
- **Documentation de l'API :** http://localhost:8080/scalar (ouverte directement au lancement depuis Visual Studio).

## Annuaire (lot 2)

- **Recherche :** http://localhost:4200/etablissements (catégorie, région › ville › commune, texte sans accents, « autour de moi », réservation en ligne) ; les filtres sont dans l'URL.
- **Vue carte :** OpenStreetMap + Leaflet, sans clé. Les tuiles passent par le cache du serveur web (`/tiles/…`, `nginx.conf`) ; pour un fort trafic, `MAP_TILES_URL` pointe vers un fournisseur sous contrat. Chaque point affiche nom, téléphone, commune, quartier (ligne d'adresse) et nombre de prestations.
- **Fiche :** `/etablissements/{slug}` (carrousel, prestations, horaires, Appeler, S'y rendre).
- **Données de démonstration :** 10 établissements fictifs à Abidjan, créés si `Database:SeedDemoData=true` (activé en développement et dans `docker-compose.yml`, **jamais en production**).
- **Yango :** le format du lien profond n'étant pas publié, renseigner `YANGO_URL_TEMPLATE` (avec `{lat}` et `{lng}`) dans `.env` pour afficher l'option.
- Régénérer les visuels : `node scripts/generate-demo-media.cjs` ; le client TypeScript : `npm run api:types`.

## Espace partenaire et KYC (lot 3)

- **Partenaire :** s'inscrire avec le profil « Professionnel » (`/inscription?type=partenaire`), puis `/pro` : assistant en 6 étapes (infos, localisation, horaires, photos, pièce d'identité, récapitulatif), prestations (`/pro/etablissements/{id}/prestations`) et galerie (`.../galerie`).
- **Admin :** `/admin/kyc` (file des dossiers, visionneuse des pièces, décision). Un compte `Admin` est créé par un SuperAdmin (le SuperAdmin initial se définit via `ADMIN_EMAIL` / `ADMIN_PASSWORD`).
- **Stockage :** MinIO (`public-media` pour les photos, servies via `/api/v1/files/...`, `private-kyc` pour les pièces, jamais publiques). Sans MinIO (tests), `Storage:Provider=Memory`.
- **Limite de photos :** `Media:MaxPhotos` (5 par défaut, en attendant les plans d'abonnement du lot 6).

## Rendez-vous (lot 4)

- **Client :** bouton « Réserver » sur la fiche → `/etablissements/{slug}/reserver` (prestations, professionnel, jour et heure, téléphone pour le SMS) ; `/compte/rendez-vous` pour suivre, annuler ou répondre à une reprogrammation ; cloche de notifications dans la barre de navigation.
- **Partenaire :** `/pro/etablissements/{id}/agenda` (demandes en direct, agenda, ressources, indisponibilités, paramètres de réservation, statistiques). La réservation s'active dans « Paramètres » une fois la fiche publiée, avec au moins une prestation réservable et des horaires.
- **Garanties :** une contrainte d'exclusion PostgreSQL rend la double réservation impossible (409 « Ce créneau vient d'être réservé ») ; les calendriers ouverts se mettent à jour en direct (SignalR, `/hubs/*`).
- **SMS :** journalisés en développement (`LoggingSmsSender`) ; branchez Infobip ou Twilio derrière `ISmsSender`.

## Abonnements et paiement (lot 5)

- **Formules :** Gratuit et Premium (réservation en ligne, statistiques, mise en avant, 30 photos), modifiables par un admin (`PUT /api/v1/admin/plans/{id}`) ; un changement de prix n'affecte pas les abonnements en cours. Page publique `/tarifs`, espace partenaire `/pro/etablissements/{id}/abonnement`.
- **Paiement :** passerelle `IPaymentGateway`. `Payments:Provider=Fake` (développement et tests : page `/paiement/simulation`, refusée en production) `Winipay` (`Payments:Winipay:ApplyKey|Token|Env`, voir `docs/DEPLOYMENT.md` §7) ou `CinetPay` ; `Winipay` a été **vérifié de bout en bout sur son bac à sable** (abonnement, changement de formule ; PROD, virements et remboursements non vérifiés), `CinetPay` reste écrit d'après sa documentation et **non testé** faute de clés. Le webhook (`/api/v1/webhooks/payments/{provider}`) est idempotent, authentifie l'appel (signature ou URL de rappel signée) puis relit le statut, le montant et la devise auprès du fournisseur.
- **Cycle de vie :** engagement de 12 mois, facture suivante 3 jours avant l'échéance, rappels J-3/J0/J+3/J+7, impayé après 7 jours de grâce = réservation et statistiques fermées (la fiche reste visible), résiliation effective à la fin de l'engagement. Factures numérotées `KB-AAAA-NNNNNN`, PDF téléchargeable.
- **Tâche de fond :** `BillingMaintenance` (toutes les heures, `Billing:MaintenanceEnabled`).

## Back-office (lot 6)

- **Accès :** `/admin` (rôles Admin ou SuperAdmin ; double authentification obligatoire). Sections : dossiers KYC, utilisateurs, établissements, finances, paramètres, journal d'audit, administrateurs (SuperAdmin).
- **Motif obligatoire** pour suspendre un compte ou une fiche et retirer une photo ; tout est tracé (`admin.audit_logs`, avec IP) et consultable sous « Journal d'audit ».
- **Finances :** abonnements par formule, revenu récurrent mensuel, encaissements par moyen de paiement, impayés avec relance manuelle, export CSV.
- **Paramètres :** formules et tarifs, catégories, zones, délai de grâce d'impayé, modèles de messages (variables `{etablissement}`, `{date}`, `{heure}`, `{client}`, `{montant}`, `{facture}`…).
- **Administrateurs :** un SuperAdmin nomme un compte existant (e-mail confirmé) ; la personne configure la double authentification à sa prochaine connexion.

## Durcissement et mise en production (lot 7)

- **Documentation :** [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) (architecture, variables, sauvegardes, exploitation), [docs/PRODUCTION-CHECKLIST.md](docs/PRODUCTION-CHECKLIST.md) (ce qui reste à faire avant l'ouverture) et [docs/ETAT-DES-FONCTIONNALITES.md](docs/ETAT-DES-FONCTIONNALITES.md).
- **Sécurité :** CSP stricte et en-têtes (nginx et API), limitation de débit globale par IP réelle (`Proxy__TrustForwardedHeaders=true` derrière nginx), `Cache-Control: no-store` sur l'API.
- **Données personnelles :** consentement à l'inscription, export JSON et suppression de compte depuis « Mon compte », pages `/cgu` et `/confidentialite` (textes de départ à faire valider).
- **PWA :** manifeste, icônes et service worker (fichiers statiques seulement).
- **E-mails :** renseignez `Email__Smtp__Host` (et le reste de la section) ; sans cela ils ne sont que journalisés.

## Au-delà de la V1 (parité, lot 7)

- **Liste d'attente :** une journée complète reste sélectionnable dans le calendrier de réservation (« Me prévenir si un créneau se libère ») ; dès qu'un rendez-vous de ce jour est annulé, refusé ou expire, toutes les personnes inscrites sont prévenues et le premier qui réserve obtient le créneau. Suivi client dans `/compte/fidelite`, vue partenaire dans `/pro/etablissements/{id}/demandes`.
- **Fidélité :** chaque établissement règle sa carte (`/pro/etablissements/{id}/fidelite`) : points par tranche de 1 000 F, seuil et récompense. Les visites terminées rapportent les points (une seule fois, à partir de l'activation de la carte) ; l'établissement accorde la récompense au comptoir.
- **Cartes cadeaux :** l'établissement encaisse lui-même la carte puis la crée (code `KB-XXXX-XXXX`, 1 000 à 1 000 000 F, validité 1 à 24 mois) et la débite en une ou plusieurs fois ; la cliente l'ajoute à son compte pour suivre le solde. Saisie de codes limitée (`RateLimits:GiftCardCodesPerMinute`, 10 par défaut).
- **Réservation de groupe :** `/etablissements/{slug}/groupe` (mariage, anniversaire… de 2 à 50 personnes) ; l'établissement répond par un devis ou décline.
- **Recommandations :** « Salons près de chez vous » sur l'accueil (`GET /api/v1/recommendations`), selon la distance, la note et les catégories déjà fréquentées ; les lieux déjà visités sont écartés.
- **Ré-engagement :** message « Envie de revenir ? » envoyé une fois, 30 jours après la dernière visite (réglable dans Paramètres, 0 = désactivé), sauf si la cliente a déjà repris rendez-vous (`ReengagementMaintenance`, `Reengagement:MaintenanceEnabled`).
- **Affichage :** mode sombre (ou réglage de l'appareil) et taille du texte dans « Mon compte », mémorisés sur l'appareil.

## Mesures, sauvegardes et audits

- **Performance :** `KEKE_PERF=1 dotnet test src/backend/tests/KekeBeauty.IntegrationTests --filter PerformanceTests` charge 50 000 fiches dans une base jetable et mesure recherche et disponibilités (budgets du cahier des charges).
- **Sauvegarde :** `scripts/backup-postgres.sh [dossier]` ; `scripts/verify-restore.sh` prouve qu'une restauration fonctionne (base jetable, comparaison des volumes). Voir `docs/DEPLOYMENT.md` §4.
- **Résultats des audits** (Lighthouse, ZAP, dépendances) : `docs/PRODUCTION-CHECKLIST.md`.

## Développement local (sans conteneur pour l'API et le web)

```bash
sh scripts/init-env.sh                       # une fois : génère les secrets dans .env
docker compose up -d postgres minio
set -a; . ./.env; set +a                     # charge .env dans ce terminal
ConnectionStrings__Default="Host=localhost;Port=5434;Database=kekebeauty;Username=keke;Password=$POSTGRES_PASSWORD" \
Storage__AccessKey="$MINIO_ROOT_USER" Storage__SecretKey="$MINIO_ROOT_PASSWORD" Jwt__SigningKey="$JWT_SIGNING_KEY" \
dotnet run --project src/backend/src/KekeBeauty.Api --urls http://localhost:8080
cd src/frontend/keke-beauty-web && npm ci && npm start
```

Le serveur de dev Angular (http://localhost:4200) proxifie `/api` et `/health` vers l'API sur le port 8080.

## Qualité

```bash
cd src/frontend/keke-beauty-web
npm run lint            # ESLint (angular-eslint + règles d'accessibilité des templates)
npm run format:check    # Prettier
npm run api:types       # régénère le client TypeScript depuis http://localhost:8080/openapi/v1.json
cd ../../.. && dotnet format KekeBeauty.sln --verify-no-changes
```

PrimeNG est épinglé en **21.1.10 (MIT, version gratuite, sans clé de licence)** avec Angular 21 : PrimeNG 22 exige une licence PrimeUI.

## Tests

```bash
dotnet test KekeBeauty.sln                  # unitaires + intégration (Testcontainers, Docker requis)
cd src/frontend/keke-beauty-web && npx ng test --no-watch
```

## Ouvrir dans Visual Studio

Ouvrir `KekeBeauty.sln` (racine) : il regroupe l'API, le SharedKernel, l'Infrastructure, les 8 modules, les tests
et le frontend Angular (projet JavaScript `keke-beauty-web.esproj`, Visual Studio 2022 17.8+ avec la charge de travail Node.js).

- Lancer `npm ci` une fois dans `src/frontend/keke-beauty-web` (l'installation npm n'est pas automatique au build).
- Démarrage multiple : définir `KekeBeauty.Api` (profil http, port 8080 via `--urls` si besoin) **et** `keke-beauty-web` comme projets de démarrage.

## Structure

```
KekeBeauty.sln   Solution Visual Studio (projets .NET)
src/backend/     .NET 10 : Api, SharedKernel, Infrastructure, 8 modules métier, tests
src/frontend/    Workspace Angular + PrimeNG (preset Keke Beauty)
specs/           Artefacts Spec Kit (spec, plan, tâches) par lot
```
