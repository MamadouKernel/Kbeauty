# Keke Beauty

Annuaire géolocalisé de salons de beauté (B2C clients + B2B établissements), avec prise de RDV et
abonnement. Voir le cahier des charges : `CDC_Keke Beauty.pdf`.

## Méthodologie

- **Spec-Driven Development** : [Spec Kit](https://github.com/github/spec-kit) — constitution →
  specify → plan → tasks → implement. Voir [.specify/memory/constitution.md](.specify/memory/constitution.md).
- **Conception des données** : MERISE complet (RG, DD, MCD, MLD, MPD, MCT, MOT) — voir [docs/merise/](docs/merise/).
- **Gestion de projet** : Scrum — voir [docs/scrum/](docs/scrum/) (Product Backlog, Sprints).

## Base de données

PostgreSQL 16, conteneurisé via Docker Desktop.

```bash
cp .env.example .env         # première fois seulement
docker compose up -d postgres
./scripts/db/migrate.sh
```

Vérifier le schéma :

```bash
docker exec kekebeauty-postgres psql -U kekebeauty -d kekebeautyDb -c "\dt"
```

Toute évolution du schéma se fait en ajoutant un nouveau fichier `db/migrations/NNNN_description.sql`
puis en relançant `./scripts/db/migrate.sh` — jamais par modification manuelle directe.

Détails : [specs/001-infra-postgres/quickstart.md](specs/001-infra-postgres/quickstart.md).

## API backend (.NET 8)

Architecture en couches : `KekeBeauty.Domain` (entités) → `KekeBeauty.Application` (interfaces/cas
d'usage) → `KekeBeauty.Infrastructure` (accès données, Dapper/Npgsql) → `KekeBeauty.Api` (ASP.NET Core).

**Convention pour ajouter une future capacité métier** (ex. authentification, annuaire, RDV) :
1. Entité dans `src/KekeBeauty.Domain/Entities/` (déjà présentes, une par table MERISE)
2. Interface + cas d'usage dans `src/KekeBeauty.Application/<Domaine>/`
3. Implémentation (Dapper) dans `src/KekeBeauty.Infrastructure/<Domaine>/`
4. Contrôleur/endpoint dans `src/KekeBeauty.Api/`

```bash
docker compose up -d --build
curl http://localhost:${API_PORT:-5080}/health       # sante applicative
curl http://localhost:${API_PORT:-5080}/health/db    # lecture/ecriture reelle sur la base
```

Détails : [specs/002-scaffold-dotnet/quickstart.md](specs/002-scaffold-dotnet/quickstart.md).

### Authentification client (OTP WhatsApp via Zavu)

```bash
curl -X POST http://localhost:${API_PORT:-5080}/auth/otp/request -H "Content-Type: application/json" -d '{"telephone":"+225XXXXXXXXXX"}'
curl -X POST http://localhost:${API_PORT:-5080}/auth/otp/verify  -H "Content-Type: application/json" -d '{"telephone":"+225XXXXXXXXXX","code":"123456"}'
```

⚠️ **Dépendance externe** : l'envoi réel du code passe par WhatsApp via [Zavu](https://zavu.dev). Tant
que le projet Zavu Keke Beauty n'est pas configuré (`Zavu:ApiKey` dans `.env`), `/auth/otp/request`
retourne `502 send_failed` de façon explicite — comportement attendu, pas un bug.

Détails : [specs/003-auth-client/quickstart.md](specs/003-auth-client/quickstart.md).

### Inscription et validation KYC des établissements partenaires

```bash
curl -X POST http://localhost:${API_PORT:-5080}/partners/applications \
  -F "telephone=+225XXXXXXXXXX" -F "nomEtablissement=..." -F "gpsLatitude=..." -F "gpsLongitude=..." \
  -F "numeroServiceClient=..." -F "photoDevanture=@./devanture.jpg" -F "pieceIdentite=@./piece.jpg"

# Connexion nominative (conserver le champ token renvoye dans ADMIN_TOKEN)
curl -X POST -H "Content-Type: application/json" -d '{"email":"admin@kekebeauty.ci","motDePasse":"..."}' "http://localhost:${API_PORT:-5080}/admin/auth/login"

# Endpoints admin proteges par jeton signe et roles
curl -H "X-Admin-Token: $ADMIN_TOKEN" "http://localhost:${API_PORT:-5080}/admin/applications?statut=EN_ATTENTE"
curl -X POST -H "X-Admin-Token: $ADMIN_TOKEN" "http://localhost:${API_PORT:-5080}/admin/applications/<id>/validate"
```

Les endpoints /admin/* utilisent des comptes nominatifs, un jeton signé de 30 minutes et les rôles
SUPER_ADMIN, KYC, SUPPORT et COMPTABLE. ADMIN_API_KEY sert uniquement de mot de passe
du premier super-administrateur lorsque la table des comptes est vide ; configurez aussi un
ADMIN_SIGNING_KEY long et aléatoire. La notification de rejet au gérant dépend de Zavu.

Détails : [specs/004-onboarding-partenaire/quickstart.md](specs/004-onboarding-partenaire/quickstart.md).

### Recherche et fiche établissement (annuaire client)

```bash
curl "http://localhost:${API_PORT:-5080}/etablissements?categorie=Spa&commune=Cocody"
curl "http://localhost:${API_PORT:-5080}/etablissements/<id>"

# Mecanisme minimal temporaire (FR-008) pour tester la recherche sans gestion partenaire encore construite
curl -X POST -H "X-Admin-Token: $ADMIN_TOKEN" -H "Content-Type: application/json" \
  -d '{"libelleCategorie":"Spa"}' "http://localhost:${API_PORT:-5080}/admin/applications/<id>/categories"
curl -X POST -H "X-Admin-Token: $ADMIN_TOKEN" -H "Content-Type: application/json" \
  -d '{"libellePrestation":"...","tarif":15000,"dureeMinutes":60}' \
  "http://localhost:${API_PORT:-5080}/admin/applications/<id>/prestations"
```

Seuls les établissements `VALIDE` avec au moins une catégorie apparaissent en recherche ; la fiche
d'un établissement non validé répond `404`, identique à un identifiant inexistant (pas de fuite
d'information).

Détails : [specs/005-recherche-annuaire/quickstart.md](specs/005-recherche-annuaire/quickstart.md).

### Gestion des prestations/catégories par le partenaire (self-service)

```bash
# OTP generalise au type PARTENAIRE (feature 006)
curl -X POST http://localhost:${API_PORT:-5080}/auth/otp/request -H "Content-Type: application/json" \
  -d '{"telephone":"+225XXXXXXXXXX","typeCompte":"Partenaire"}'

curl -X POST -H "X-Partner-Id: $PARTNER_ID" -H "Content-Type: application/json" \
  -d '{"libellePrestation":"...","tarif":5000,"dureeMinutes":30}' \
  "http://localhost:${API_PORT:-5080}/partenaire/etablissements/<id>/prestations"
```

⚠️ **Dette technique documentée** : `X-Partner-Id` (l'`idUtilisateur` obtenu après OTP) tient lieu de
preuve d'identité — aucune session/JWT n'est encore construite dans le projet (même compromis que la
clé admin). Toute action est vérifiée contre `id_utilisateur_gerant` de l'établissement cible (`403`
sinon). Remplace le mécanisme admin temporaire de la feature 005 pour l'usage courant.

Détails : [specs/006-gestion-prestations/quickstart.md](specs/006-gestion-prestations/quickstart.md).

### Prise de rendez-vous

```bash
curl "http://localhost:${API_PORT:-5080}/etablissements/<id>/creneaux?date=2026-09-20"

curl -X POST -H "X-Client-Id: $CLIENT_ID" -H "Content-Type: application/json" \
  -d '{"idEtablissement":"<id>","idPrestation":"<idPrestation>","dateHeureDebut":"2026-09-20T10:00:00Z"}' \
  http://localhost:${API_PORT:-5080}/rdv

curl -X POST -H "X-Partner-Id: $PARTNER_ID" "http://localhost:${API_PORT:-5080}/partenaire/etablissements/<id>/rdv/<idRdv>/confirmer"
```

Le chevauchement de créneaux est détecté de façon atomique en base (`INSERT ... WHERE NOT EXISTS`)
pour éviter toute double réservation en cas de demandes simultanées. `X-Client-Id` suit le même
compromis technique que `X-Partner-Id` (pas de session/JWT).

Détails : [specs/007-prise-rdv/quickstart.md](specs/007-prise-rdv/quickstart.md).

### Abonnement et paiement

```bash
curl -X POST -H "X-Partner-Id: $PARTNER_ID" -H "Content-Type: application/json" \
  -d '{"periodicite":"MENSUEL"}' \
  "http://localhost:${API_PORT:-5080}/etablissements/<id>/abonnements"
# -> { "idAbonnement", "statut": "IMPAYE", "checkoutUrl": "https://checkout.winipayer.com/..." }

curl -H "X-Admin-Token: $ADMIN_TOKEN" "http://localhost:${API_PORT:-5080}/admin/abonnements?statut=IMPAYE"
curl -X POST -H "X-Admin-Token: $ADMIN_TOKEN" "http://localhost:${API_PORT:-5080}/admin/abonnements/<id>/relance"
curl -H "X-Admin-Token: $ADMIN_TOKEN" "http://localhost:${API_PORT:-5080}/admin/tarifs"
curl -X PUT -H "X-Admin-Token: $ADMIN_TOKEN" -H "Content-Type: application/json" \
  -d '{"montant":50000}' "http://localhost:${API_PORT:-5080}/admin/tarifs/ANNUEL"
```

L'agrégateur de paiement est **WiniPayer** (compte marchand "Keke Beauty" créé, environnement TEST
— voir `.env` `WINIPAYER_*`). WiniPayer fonctionne par lien de paiement hébergé : la souscription
crée l'abonnement `IMPAYE` et retourne `checkoutUrl` (à ouvrir pour payer) ; le résultat réel arrive
via `POST /webhooks/winipayer/callback`, signé (`sha256(privateKey+uuid+crypto+amount+created_at)`)
et idempotent. Un seul abonnement `ACTIF` par établissement, vérifié atomiquement en base (même
pattern que le chevauchement de RDV). La modification du tarif standard n'affecte que les
souscriptions futures. Si un callback est raté, `POST /admin/abonnements/<id>/verifier-paiement`
interroge directement WiniPayer pour réconcilier. Le passage en PROD reste explicitement désactivé
tant que `WINIPAYER_PROD_TOKEN_KEY`/`WINIPAYER_PROD_PRIVATE_KEY` ne sont pas renseignées.

Détails : [specs/008-abonnement-paiement/quickstart.md](specs/008-abonnement-paiement/quickstart.md).

### Modération back-office (suspension/réactivation)

```bash
curl -X POST -H "X-Admin-Token: $ADMIN_TOKEN" "http://localhost:${API_PORT:-5080}/admin/utilisateurs/<id>/suspendre"
curl -X POST -H "X-Admin-Token: $ADMIN_TOKEN" "http://localhost:${API_PORT:-5080}/admin/etablissements/<id>/suspendre"
```

Un compte suspendu ne peut plus demander de code OTP (`403`). Un établissement suspendu disparaît de
l'annuaire et ne peut plus recevoir de nouvelle demande de RDV ni de souscription d'abonnement. La
suspension est orthogonale à `statutKyc` : la réactivation restaure exactement l'état antérieur.

Détails : [specs/009-moderation-back-office/quickstart.md](specs/009-moderation-back-office/quickstart.md).

## Frontend web (Blazor — parcours client)

```bash
docker compose up -d --build api web
```

Ouvrir `http://localhost:${WEB_PORT:-5090}` : recherche annuaire, fiche établissement (appel,
itinéraire, prestations), connexion OTP, prise de RDV. Architecture : `KekeBeauty.Web` (Blazor Web
App, render mode Server) appelle `KekeBeauty.Api` via `HttpClient` (config `Api:BaseUrl`, réseau
Docker interne — pas de CORS). Session client (`idUtilisateur` après OTP) conservée en
`ProtectedLocalStorage`, cohérent avec la dette technique déjà documentée côté API (`X-Client-Id`,
pas de vraie session/JWT).

⚠️ Le parcours OTP dépend de Zavu (même dépendance externe que le reste du projet) : sans
`Zavu:ApiKey` configurée, la demande de code échoue de façon explicite dans l'interface (message
d'erreur visible, jamais un écran vide).

Détails : [specs/010-frontend-client-annuaire-rdv/quickstart.md](specs/010-frontend-client-annuaire-rdv/quickstart.md).

## Frontend web (Blazor — parcours partenaire)

Toujours dans `KekeBeauty.Web`. Ouvrir `http://localhost:${WEB_PORT:-5090}/partenaire/login` :
connexion OTP (type PARTENAIRE), tableau de bord listant les établissements du gérant, gestion des
prestations (ajout/modification/suppression) et catégories, consultation et traitement des
demandes de RDV (confirmer/refuser).

Deux endpoints backend minimaux ont été ajoutés pour ce parcours (aucun n'existait) :
`GET /partenaire/etablissements` (découverte "mes établissements") et
`GET /partenaire/etablissements/{id}/rdv` (liste des RDV, protégé par `PartnerOwnershipFilter`
existant). Même dette technique que le reste du projet (`X-Partner-Id`, pas de session/JWT).

Détails : [specs/011-frontend-partenaire/quickstart.md](specs/011-frontend-partenaire/quickstart.md).

## Frontend web (Blazor — parcours admin)

Toujours dans `KekeBeauty.Web`. Ouvrir `http://localhost:${WEB_PORT:-5090}/admin/login` :
connexion par clé API admin, gestion des dossiers KYC (liste/détail/validation/rejet), modération
(suspension/réactivation par identifiant), suivi et tarification des abonnements. Aucun ajout
backend n'a été nécessaire — l'API admin existante (004/008/009) couvre tous les besoins.

⚠️ **Bug de prerendering corrigé** : toute page Blazor qui lit une session (`ProtectedLocalStorage`)
dans `OnInitializedAsync` doit désactiver le prerendering statique
(`@rendermode @(new InteractiveServerRenderMode(prerender: false))`), sinon la lecture échoue
silencieusement pendant le rendu statique et provoque une redirection immédiate vers la page de
connexion. Appliqué à toutes les pages concernées (client, partenaire, admin).

Détails : [specs/012-frontend-admin/quickstart.md](specs/012-frontend-admin/quickstart.md).

## pgAdmin (optionnel)

```bash
docker compose up -d pgadmin
```

Accessible sur `http://localhost:${PGADMIN_PORT:-5050}` (identifiants dans `.env`).

### Connexion et création de compte avec Google

Définissez `GOOGLE_CLIENT_ID` avec l'identifiant d'un client OAuth 2.0 de type **Application Web**,
puis ajoutez les origines du site (par exemple `http://localhost:5090` en développement et le
domaine HTTPS de production) dans **Origines JavaScript autorisées** de Google Cloud Console.
Le même Client ID est transmis à l'interface et à l'API. L'API valide la signature, l'audience et
l'adresse email vérifiée du jeton Google avant de créer ou retrouver le compte client. Aucun secret
Google n'est exposé au navigateur et le bouton n'est pas affiché lorsque le Client ID est absent.