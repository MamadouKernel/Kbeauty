# Phase 1 — Quickstart: Infrastructure Base de Données Reproductible

Guide de validation manuelle de la feature (correspond aux Acceptance Scenarios de `spec.md`).

## Prérequis
- Docker Desktop installé et démarré.
- Fichier `.env` présent à la racine (copié depuis `.env.example`).

## Scénario 1 — Démarrage en une commande (US-01)

```bash
docker compose up -d postgres
./scripts/db/migrate.sh
```

**Résultat attendu** : le conteneur `kekebeauty-postgres` est `healthy`, et la commande suivante liste
les 13 tables métier + `schema_migrations` :

```bash
docker exec kekebeauty-postgres psql -U kekebeauty -d kekebeauty -c "\dt"
```

## Scénario 2 — Persistance au redémarrage (US-01)

```bash
docker compose restart postgres
docker exec kekebeauty-postgres psql -U kekebeauty -d kekebeauty -c "SELECT count(*) FROM schema_migrations;"
```

**Résultat attendu** : le compteur reste identique (aucune perte de données, aucune ré-application).

## Scénario 3 — Reconstruction depuis zéro (US-02)

```bash
docker compose down -v      # supprime le volume : simule un environnement vide
docker compose up -d postgres
./scripts/db/migrate.sh
```

**Résultat attendu** : schéma final identique à celui obtenu avant suppression (mêmes tables,
mêmes contraintes) — reproductible sans intervention manuelle.

## Scénario 4 — Détection d'incohérence (Edge Case / FR-007)

```bash
# Modifier à la main un fichier de migration déjà appliqué, puis :
./scripts/db/migrate.sh
```

**Résultat attendu** : le script échoue explicitement avec un message signalant le checksum invalide,
sans appliquer aucune autre migration.
