# Phase 1 — Quickstart: Squelette Applicatif Backend

## Prérequis
- Feature `001-infra-postgres` opérationnelle (`docker compose up -d postgres && ./scripts/db/migrate.sh`).

## Scénario 1 — Démarrage en une commande (US1)

```bash
docker compose up -d --build
```

**Résultat attendu** : les conteneurs `kekebeauty-postgres` et `kekebeauty-api` sont démarrés.

```bash
curl http://localhost:${API_PORT:-5080}/health
```

**Résultat attendu** : réponse HTTP 200 avec un statut `Healthy`.

## Scénario 2 — Vérification lecture/écriture base de données (US2)

```bash
curl http://localhost:${API_PORT:-5080}/health/db
```

**Résultat attendu** : réponse HTTP 200 confirmant lecture ET écriture réussies, sans ligne persistée
dans `health_check` (vérifiable via `docker exec kekebeauty-postgres psql -U kekebeauty -d kekebeautyDb -c "SELECT count(*) FROM health_check;"` → `0`).

## Scénario 3 — Base de données indisponible (Edge Case / FR-004)

```bash
docker compose stop postgres
curl http://localhost:${API_PORT:-5080}/health/db
docker compose start postgres
```

**Résultat attendu** : réponse HTTP non-200 (ex. 503) avec un message explicite d'indisponibilité,
pas une exception non gérée ni un succès trompeur.

## Scénario 4 — Développement local hors Docker (complément, débogage IDE)

```bash
cd src/KekeBeauty.Api
dotnet run
```

**Résultat attendu** : identique au Scénario 1/2, utile pour déboguer depuis Visual Studio/Rider/VS Code.
