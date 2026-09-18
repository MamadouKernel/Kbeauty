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

# Endpoints admin (proteges par cle temporaire, voir Assumptions)
curl -H "X-Admin-Api-Key: $ADMIN_API_KEY" "http://localhost:${API_PORT:-5080}/admin/applications?statut=EN_ATTENTE"
curl -X POST -H "X-Admin-Api-Key: $ADMIN_API_KEY" "http://localhost:${API_PORT:-5080}/admin/applications/<id>/validate"
```

⚠️ **Dette technique documentée** : les endpoints `/admin/*` sont protégés par une simple clé d'API
statique (`ADMIN_API_KEY`), en attendant une vraie feature d'authentification administrateur (aucune
n'existe encore dans le projet). Ne jamais exposer cette clé publiquement. La notification de rejet
au gérant dépend de Zavu (même comportement que l'OTP client : échec explicite si non configuré).

Détails : [specs/004-onboarding-partenaire/quickstart.md](specs/004-onboarding-partenaire/quickstart.md).

### Recherche et fiche établissement (annuaire client)

```bash
curl "http://localhost:${API_PORT:-5080}/etablissements?categorie=Spa&commune=Cocody"
curl "http://localhost:${API_PORT:-5080}/etablissements/<id>"

# Mecanisme minimal temporaire (FR-008) pour tester la recherche sans gestion partenaire encore construite
curl -X POST -H "X-Admin-Api-Key: $ADMIN_API_KEY" -H "Content-Type: application/json" \
  -d '{"libelleCategorie":"Spa"}' "http://localhost:${API_PORT:-5080}/admin/applications/<id>/categories"
curl -X POST -H "X-Admin-Api-Key: $ADMIN_API_KEY" -H "Content-Type: application/json" \
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

## pgAdmin (optionnel)

```bash
docker compose up -d pgadmin
```

Accessible sur `http://localhost:${PGADMIN_PORT:-5050}` (identifiants dans `.env`).
