---

description: "Task list template for feature implementation"
---

# Tasks: Infrastructure Base de Données Reproductible

**Input**: Design documents from `/specs/001-infra-postgres/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: Non explicitement demandés dans la spec — aucune tâche de test automatisé générée ;
`quickstart.md` sert de plan de validation manuelle.

**Organization**: Tâches groupées par User Story (US1 = P1, US2 = P2).

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [x] T001 Créer `docker-compose.yml` (service `postgres` + `pgadmin`) à la racine du projet
- [x] T002 Créer `.env.example` avec les variables `POSTGRES_*` et `PGADMIN_*`
- [x] T003 Créer `.gitignore` excluant `.env`

## Phase 2: Foundational (bloquant pour toutes les User Stories)

- [ ] T004 Créer `scripts/db/migrate.sh` : script bash qui (1) attend que Postgres soit prêt
  (`pg_isready`), (2) crée la table `schema_migrations` si absente, (3) parcourt `db/migrations/*.sql`
  par ordre de nom de fichier, (4) pour chaque fichier non présent dans `schema_migrations`, calcule
  son SHA-256, l'applique dans une transaction, puis insère `(version, description, checksum, now())`
  — voir `data-model.md`
- [ ] T005 Dans `scripts/db/migrate.sh`, ajouter la vérification de checksum : si un fichier déjà
  appliqué a un checksum différent de celui stocké, le script MUST échouer explicitement avant
  d'appliquer quoi que ce soit d'autre (FR-007)
- [ ] T006 Rendre `scripts/db/migrate.sh` exécutable (`chmod +x`) et documenter son usage en tête de
  fichier (commentaire) : prérequis, variables d'environnement lues, codes de sortie

## Phase 3: User Story 1 - Démarrage d'un environnement de base de données local (Priority: P1) 🎯 MVP

**Goal**: Un développeur peut obtenir un environnement PostgreSQL complet (13 tables métier) en une
seule commande, données persistées entre redémarrages.

**Independent Test**: `docker compose up -d postgres && ./scripts/db/migrate.sh` sur une machine sans
base existante, puis vérifier via `psql \dt` que toutes les tables du MPD sont présentes.

- [x] T007 [US1] Créer `db/init/000_bootstrap.sql` : script minimal exécuté une seule fois par Postgres
  au premier démarrage du volume — se limite à `CREATE EXTENSION IF NOT EXISTS "pgcrypto";` (le reste
  du schéma est désormais porté par les migrations, pas par `docker-entrypoint-initdb.d`)
- [ ] T008 [P] [US1] Créer `db/migrations/0001_init_schema.sql` en reprenant exactement le contenu du
  MPD validé (`docs/merise/05-mpd.sql`) : 13 tables métier, enums, contraintes, index
- [ ] T009 [US1] Exécuter le Scénario 1 de `quickstart.md` (démarrage) et vérifier les 13 tables + `schema_migrations`
- [ ] T010 [US1] Exécuter le Scénario 2 de `quickstart.md` (persistance après `docker compose restart`)

**Checkpoint**: À ce stade, US1 est livrable de façon autonome — un développeur peut démarrer et
utiliser l'environnement.

## Phase 4: User Story 2 - Évolution traçable du schéma (Priority: P2)

**Goal**: Toute évolution future du schéma est un fichier de migration versionné, rejouable depuis
un environnement vide, sans jamais modifier une base existante à la main.

**Independent Test**: Partir d'un environnement vide (`docker compose down -v`), rejouer toutes les
migrations dans l'ordre, obtenir un schéma identique à celui d'un environnement existant.

- [ ] T011 [US2] Documenter dans `scripts/db/migrate.sh` (commentaire d'en-tête) la convention de
  nommage des futures migrations : `NNNN_description.sql`, `NNNN` strictement croissant
- [ ] T012 [US2] Exécuter le Scénario 3 de `quickstart.md` (reconstruction depuis zéro via
  `docker compose down -v` puis remontée + migration) et confirmer l'identité du schéma obtenu
- [ ] T013 [US2] Exécuter le Scénario 4 de `quickstart.md` (modification a posteriori d'une migration
  déjà appliquée) et confirmer l'échec explicite du script (FR-007)

**Checkpoint**: US1 + US2 livrées — le socle de données du projet est complet et évolutif.

## Phase 5: Polish & Cross-Cutting Concerns

- [ ] T014 [P] Ajouter au `README.md` racine (à créer si absent) une section "Base de données" avec
  les commandes de démarrage (`docker compose up -d postgres`, `./scripts/db/migrate.sh`)
- [ ] T015 Mettre à jour `docs/scrum/sprint-0.md` : cocher la ligne "US-02 — Schéma appliqué" une fois
  T004 à T013 terminées

## Dependencies & Execution Order

- Setup (Phase 1) : déjà fait (T001-T003).
- Foundational (Phase 2, T004-T006) : bloque toutes les User Stories — le script de migration doit
  exister avant de pouvoir exécuter T008-T013.
- User Story 1 (Phase 3) : dépend de la Phase 2. Peut être livrée seule (MVP).
- User Story 2 (Phase 4) : dépend de la Phase 3 (US1 doit être fonctionnelle avant de tester
  l'évolution du schéma qu'elle contient).
- Polish (Phase 5) : après les deux User Stories.

## Parallel Example

```text
T008 [P] [US1] peut être rédigé en parallèle de T007 (fichiers distincts, aucune dépendance directe)
T014 [P] peut être rédigé en parallèle de T015 (fichiers distincts)
```

## Implementation Strategy

**MVP first** : implémenter et valider entièrement la Phase 3 (US1) avant d'attaquer la Phase 4 (US2).
US1 seule livre déjà de la valeur (environnement de dev partageable) ; US2 sécurise l'évolution future
mais n'est pas bloquante pour démarrer le développement fonctionnel (Epic 2 et suivants du Product Backlog).
