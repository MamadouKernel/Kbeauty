# Phase 0 — Research: Squelette Applicatif Backend

## Décision 1 — Framework et version

- **Decision**: ASP.NET Core 8 (.NET 8 LTS), C# 12.
- **Rationale**: Stack imposé par le porteur du projet ("Full .NET/C#"). .NET 8 est la version LTS
  actuelle, support jusqu'en 2026+, image Docker officielle légère (`aspnet:8.0`).
- **Alternatives envisagées**: .NET 9 (non-LTS, support plus court) — rejeté pour un socle de production.

## Décision 2 — Accès aux données : Dapper plutôt qu'Entity Framework Core

- **Decision**: Dapper + Npgsql pour l'accès aux données, pas d'ORM à migrations intégrées.
- **Rationale**: Le projet a déjà un système de migrations SQL versionnées et testé
  (`scripts/db/migrate.sh`, Principe III de la constitution). Ajouter EF Core Migrations créerait
  deux sources de vérité concurrentes pour l'évolution du schéma (violerait DRY et la constitution).
  Dapper reste un mappage léger au-dessus du SQL déjà écrit en MERISE (MPD), sans dupliquer le schéma
  en C# via des migrations séparées.
- **Alternatives envisagées**: EF Core (rejeté pour la raison ci-dessus, réévaluable si le projet
  grandit et qu'un besoin de requêtes complexes typées devient prioritaire) ; ADO.NET brut (rejeté,
  Dapper apporte le même niveau de contrôle avec moins de code répétitif).

## Décision 3 — Vérification de connexion base de données (US2 / FR-003, FR-004)

- **Decision**: Un endpoint dédié `/health/db` exécute (1) un `SELECT 1` pour la lecture, puis (2) une
  transaction `INSERT` suivie d'un `ROLLBACK` dans une table technique dédiée `health_check`, pour
  prouver la capacité d'écriture sans laisser de données résiduelles.
- **Rationale**: Répond explicitement à FR-003 (lecture ET écriture réelles) sans polluer les tables
  métier ni nécessiter un nettoyage applicatif après coup (le ROLLBACK annule immédiatement l'écriture).
- **Alternatives envisagées**: Vérifier uniquement la lecture (rejeté — ne couvre pas FR-003) ; écrire
  puis supprimer explicitement (rejeté — plus complexe et moins sûr en cas d'échec entre les deux
  étapes qu'un simple ROLLBACK transactionnel).

## Décision 4 — Démarrage en une commande (US1 / FR-001)

- **Decision**: Ajout d'un service `api` dans le `docker-compose.yml` existant (build depuis
  `src/KekeBeauty.Api/Dockerfile`), de sorte que `docker compose up` démarre à la fois la base de
  données et l'API.
- **Rationale**: Conserve la promesse "une seule commande" déjà établie par la feature 001, sans
  introduire une deuxième commande de démarrage distincte à mémoriser.
- **Alternatives envisagées**: `dotnet run` local hors Docker — rejeté comme mécanisme principal
  (réintroduirait une dépendance au SDK .NET installé localement là où Docker suffit déjà), mais reste
  possible en complément pour le débogage IDE (documenté dans quickstart.md).

## Décision 5 — Attente de la disponibilité de la base au démarrage (Edge Case)

- **Decision**: Health check ASP.NET Core avec `AspNetCore.HealthChecks.NpgSql`, configuré pour ne pas
  faire échouer le démarrage du processus si la base n'est pas encore prête — l'API démarre et son
  endpoint `/health/db` reflète l'indisponibilité jusqu'à ce que la base réponde.
- **Rationale**: Évite un crash au démarrage si `docker compose up` lance l'API avant que Postgres
  n'ait fini son propre démarrage (dépendance `depends_on` avec `condition: service_healthy` dans
  `docker-compose.yml` réduit ce risque, mais le comportement de repli reste nécessaire).
- **Alternatives envisagées**: Faire échouer le démarrage si la base n'est pas immédiatement joignable
  — rejeté, contraire à l'edge case de la spec qui exige un signalement explicite plutôt qu'un plantage.

Toutes les inconnues techniques sont résolues ; prêt pour la Phase 1 (Design & Contracts).
