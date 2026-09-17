# Phase 0 — Research: Infrastructure Base de Données Reproductible

Aucun marqueur `[NEEDS CLARIFICATION]` n'a été laissé dans `spec.md` ; les choix ci-dessous documentent
les décisions techniques prises pour satisfaire les exigences fonctionnelles.

## Décision 1 — Mécanisme de migration

- **Decision**: Migrations SQL brutes, numérotées séquentiellement (`NNNN_description.sql`), appliquées
  par un script shell (`scripts/db/migrate.sh`) qui journalise chaque migration exécutée dans une table
  `schema_migrations (version, applied_at)`.
- **Rationale**: Le projet n'a pas encore choisi de framework applicatif (backend) — imposer un ORM ou
  un outil de migration lié à un langage serait prématuré (violerait YAGNI). Le SQL brut est aussi le
  format le plus proche du MPD déjà produit par MERISE (`docs/merise/05-mpd.sql`), donc zéro traduction
  supplémentaire.
- **Alternatives envisagées** :
  - *Flyway/Liquibase* : robuste mais ajoute une dépendance Java non justifiée à ce stade.
  - *ORM applicatif (Prisma/TypeORM/Alembic)* : lierait le schéma au choix futur du langage backend,
    contraire au principe YAGNI et prématuré avant que ce choix ne soit fait.
  - Rejetées pour l'instant ; réévaluable si un ORM est adopté plus tard (nouvelle feature dédiée).

## Décision 2 — Application du schéma initial

- **Decision**: Le schéma initial (13 tables du MPD) devient la migration `0001_init_schema.sql`,
  appliquée par `migrate.sh` — pas par `docker-entrypoint-initdb.d` (qui ne s'exécute qu'une seule fois
  sur un volume vide et ne peut donc pas servir de mécanisme d'évolution traçable, cf. FR-004/FR-005).
- **Rationale**: `docker-entrypoint-initdb.d` seul satisferait US-01 mais pas US-02 (impossible de
  rejouer une évolution sur un volume déjà initialisé sans le détruire).
- **Alternatives envisagées** :
  - Tout dans `docker-entrypoint-initdb.d` (rejeté : pas d'évolution traçable après le premier démarrage).
  - Migration via conteneur one-off `docker compose run migrate` (retenu comme option d'exécution du
    script, complémentaire — pas incompatible avec la Decision 1).

## Décision 3 — Port réseau configurable (Edge Case)

- **Decision**: Le port PostgreSQL exposé reste piloté par la variable d'environnement `POSTGRES_PORT`
  (déjà en place dans `.env.example` et `docker-compose.yml`), avec une valeur par défaut différente de
  5432 si un conflit est détecté localement (déjà rencontré : 5433 utilisé sur ce poste).
- **Rationale**: Répond à FR-006 sans toucher au schéma ni au code applicatif.
- **Alternatives envisagées**: Port fixe non paramétrable — rejeté, casserait FR-006 dès qu'un autre
  service Postgres tourne sur la machine du développeur (cas déjà observé).

## Décision 4 — Détection d'ordre incorrect / conflit (Edge Cases)

- **Decision**: `migrate.sh` calcule et stocke un hash (ex. `sha256`) du contenu de chaque migration
  appliquée dans `schema_migrations`. Si un fichier déjà appliqué a été modifié, ou si un nouveau
  fichier porte un numéro déjà utilisé, le script échoue explicitement avant toute exécution SQL.
- **Rationale**: Répond directement à FR-007 (échec explicite plutôt qu'état partiel silencieux).
- **Alternatives envisagées**: Aucune vérification (rejeté — viole FR-007 explicitement).

Toutes les inconnues techniques sont résolues ; prêt pour la Phase 1 (Design & Contracts).
