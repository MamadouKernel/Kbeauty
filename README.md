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
docker exec kekebeauty-postgres psql -U kekebeauty -d kekebeauty -c "\dt"
```

Toute évolution du schéma se fait en ajoutant un nouveau fichier `db/migrations/NNNN_description.sql`
puis en relançant `./scripts/db/migrate.sh` — jamais par modification manuelle directe.

Détails : [specs/001-infra-postgres/quickstart.md](specs/001-infra-postgres/quickstart.md).

## pgAdmin (optionnel)

```bash
docker compose up -d pgadmin
```

Accessible sur `http://localhost:${PGADMIN_PORT:-5050}` (identifiants dans `.env`).
