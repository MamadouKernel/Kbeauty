# Sprint 1 — Squelette Applicatif Backend (Keke Beauty)

**Objectif de sprint** : disposer d'une API backend .NET démarrable en une commande, connectée à la
base de données `kekebeautyDb`, avec vérification de santé applicative et de connexion réelle —
prérequis avant le développement des premières fonctionnalités métier (US-03 et suivantes).

## Sprint Backlog

| Story | Tâches | Statut |
|---|---|---|
| US-02b — Squelette .NET | Solution + 4 projets .NET 8 (Domain/Application/Infrastructure/Api) créés | Fait |
| US-02b — Connexion base | `DbConnectionFactory` (Dapper/Npgsql), configuration externalisée | Fait |
| US-02b — Endpoint santé applicative | `GET /health` opérationnel | Fait |
| US-02b — Endpoint santé base de données | `GET /health/db` (lecture + écriture réelle, ROLLBACK) | Fait |
| US-02b — Démarrage en une commande | Service `api` ajouté à `docker-compose.yml`, `docker compose up -d --build` | Fait |
| US-02b — Structure extensible | Revue documentée (voir `specs/002-scaffold-dotnet/quickstart.md`) | Fait |

## Definition of Done du Sprint 1
- [x] `docker compose up -d --build` démarre Postgres + API sans erreur.
- [x] `GET /health` retourne `Healthy`.
- [x] `GET /health/db` confirme lecture ET écriture réelles, sans donnée persistée (`health_check` reste vide).
- [x] `GET /health/db` retourne 503 explicite si la base est indisponible (testé via arrêt/redémarrage du conteneur).
- [x] Aucune règle métier implémentée (conforme au périmètre de la feature).
- [x] Structure en couches (Domain/Application/Infrastructure/Api) vérifiée par revue.

## Prochaine étape
Lancer `/speckit-specify` sur la première feature métier du Product Backlog : US-03 (inscription
client par téléphone + OTP SMS, feature `003-auth-client`), qui sera la première à exploiter la
structure posée par `002-scaffold-dotnet`.
