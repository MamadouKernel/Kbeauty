# Implementation Plan: Avis clients après rendez-vous

**Branch**: `014-avis-clients` | **Date**: 2026-09-19 | **Spec**: [spec.md](./spec.md)

## Summary
Nouvelle table `avis` (une ligne par RDV, contrainte `UNIQUE(id_rdv)`), un endpoint de création côté
client (`POST /rdv/{id}/avis`, header `X-Client-Id`, RDV doit être TERMINE et appartenir au client) et
un endpoint public de lecture (`GET /etablissements/{id}/avis`, liste + moyenne). L'historique RDV
(013) expose un booléen `aDejaAvis` par ligne pour piloter l'UI "Mes rendez-vous".

## Technical Context
**Language/Version**: C# 12/.NET 8 (inchangé). **Storage**: PostgreSQL 16, nouvelle table `avis`
(migration `0009_avis.sql`). **Testing**: pas d'infra xUnit disponible (constat déjà fait pour 013) —
validation par `curl` contre la stack Docker, comme toutes les features de ce projet.

## Constitution Check
| Principe | Statut |
|---|---|
| I/II/IV (Spec Kit) | PASS — US1/US2 indépendamment testables, MVP = US1 seule |
| II (MERISE) | PASS — `avis` est une entité métier légitime, 1-1 avec `rdv` |
| III (migrations versionnées) | PASS — `0009_avis.sql`, checksum via `scripts/db/migrate.sh` |
| V (secrets) | PASS — aucune clé, aucun secret |

## Project Structure

```text
src/KekeBeauty.Application/Rdv/
├── IAvisRepository.cs           # nouveau : CreerAsync / ListerParEtablissementAsync / ExisteAsync
└── LaisserAvisUseCase.cs        # nouveau : verifie TERMINE + proprietaire + unicite

src/KekeBeauty.Infrastructure/Rdv/
└── AvisRepository.cs            # nouveau (Dapper)

src/KekeBeauty.Api/Controllers/
└── RdvController.cs             # + POST /rdv/{id}/avis, + GET /etablissements/{id}/avis
                                  #   (reste dans RdvController car depend de IRdvRepository pour
                                  #   la verification proprietaire/statut ; alternative rejetee :
                                  #   endpoint public isole dans EtablissementsController aurait
                                  #   duplique la logique de verification RDV)

db/migrations/0009_avis.sql

src/KekeBeauty.Web/Components/Pages/
├── MesRendezVous.razor          # + bouton "Laisser un avis" sur RDV TERMINE sans avis
└── EtablissementDetailPage.razor # + onglet Avis reel (liste + moyenne), remplace le placeholder
```

## Complexity Tracking
Aucune violation.
