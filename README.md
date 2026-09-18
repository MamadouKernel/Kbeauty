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

## pgAdmin (optionnel)

```bash
docker compose up -d pgadmin
```

Accessible sur `http://localhost:${PGADMIN_PORT:-5050}` (identifiants dans `.env`).
