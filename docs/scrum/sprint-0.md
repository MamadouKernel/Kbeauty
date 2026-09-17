# Sprint 0 — Cadrage & Fondations (Keke Beauty)

**Objectif de sprint** : disposer d'un socle technique exécutable (Spec Kit, MERISE, PostgreSQL/Docker)
et d'un backlog priorisé, avant le premier sprint de développement fonctionnel.

## Sprint Backlog

| Story | Tâches | Statut |
|---|---|---|
| US-01 — Env. PostgreSQL Docker | `docker-compose.yml` + `.env.example` créés ; `docker compose up` validé | Fait |
| US-01 — Modélisation MERISE | RG, DD, MCD, MLD, MPD, MCT, MOT rédigés (`docs/merise/`) | Fait |
| US-02 — Schéma appliqué | Migration versionnée `db/migrations/0001_init_schema.sql` appliquée via `scripts/db/migrate.sh` | Fait |
| — Gouvernance | Constitution Spec Kit ratifiée (`.specify/memory/constitution.md`) | Fait |
| — Backlog | Product Backlog initial rédigé (`docs/scrum/product-backlog.md`) | Fait |

## Definition of Done du Sprint 0
- [x] `docker compose up` démarre Postgres + pgAdmin sans erreur.
- [x] Le schéma MPD est appliqué de façon versionnée et reproductible (`db/migrations/`, `scripts/db/migrate.sh`).
- [x] La constitution du projet couvre Spec Kit, MERISE, PostgreSQL/Docker, Scrum.
- [x] Le Product Backlog couvre l'ensemble des fonctionnalités du CDC.

## Prochaine étape
Lancer `/speckit-specify` sur la première feature du backlog (`001-infra-postgres`), puis
`/speckit-plan` et `/speckit-tasks`, avant l'ouverture du Sprint 1.
