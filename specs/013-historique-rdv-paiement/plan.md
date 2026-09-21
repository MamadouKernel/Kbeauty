# Implementation Plan: Historique des rendez-vous client et paiement en ligne

**Branch**: `013-historique-rdv-paiement` | **Date**: 2026-09-19 | **Spec**: [spec.md](./spec.md)

## Summary
Liste des RDV d'un client (tous statuts, tri anti-chronologique) via une extension de `IRdvRepository` ;
paiement en ligne optionnel à la demande de RDV réutilisant tel quel `IPaymentGateway` (déjà générique,
créé pour 008) sur une nouvelle table dédiée `transaction_rdv` (le `transaction` existant est couplé en
dur à `abonnement` par une FK NOT NULL) ; webhook WiniPayer existant étendu pour reconnaître aussi les
références de paiement RDV, en plus des références d'abonnement.

## Technical Context
**Language/Version**: C# 12/.NET 8 (inchangé). **Primary Dependencies**: aucune nouvelle dépendance —
réutilise `IPaymentGateway`/`WinPayerGateway` (008) et Dapper (convention projet : classes mutables,
enums en `string` brut, jamais de `record` positionnel — cf. bug Dapper récurrent documenté).
**Storage**: PostgreSQL 16, nouvelle table `transaction_rdv` (migration `0008_transaction_rdv.sql`).
**Testing**: xUnit (`tests/KekeBeauty.Api.Tests`), pattern déjà en place pour 007/008/009.
**Target Platform**: Docker (api + web), inchangé. **Project Type**: web-service (Clean Architecture
Domain→Application→Infrastructure→Api) + Blazor Web frontend (KekeBeauty.Web).
**Constraints**: FR-009 — idempotence paiement RDV via contrainte `UNIQUE(id_rdv)` sur
`transaction_rdv` (un seul essai de paiement en ligne actif par RDV, comme pour les abonnements où
`MarquerPaiementAsync` est idempotent par `reference_externe`). FR-010 — le statut de paiement ne doit
jamais réécrire `statut_rdv` (tables et use cases strictement séparés).
**Scale/Scope**: 2 nouveaux endpoints client (`GET /rdv`, `POST /rdv/{id}/paiement/verifier`), 1
endpoint étendu (`POST /rdv` accepte un paramètre optionnel `payerEnLigne`), 1 webhook étendu, 2 pages
Blazor (Mes RDV, statut de paiement sur l'écran de confirmation existant).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principe | Statut |
|---|---|
| I/II/IV (Spec Kit, user stories priorisées) | PASS — US1/US2/US3 indépendamment testables, MVP = US1 seule |
| II (MERISE) | PASS — `transaction_rdv` est une nouvelle entité métier légitime (transaction financière liée à un RDV), symétrique de `transaction`/`abonnement` déjà modélisés ; pas de redéfinition d'une entité existante |
| III (migrations versionnées, idempotentes) | PASS — `0008_transaction_rdv.sql`, checksum SHA-256 via `scripts/db/migrate.sh` comme 0001-0007 |
| V (secrets) | PASS — aucune nouvelle clé, réutilise `Billing:WiniPayer:*` déjà en `.env` |

## Project Structure

### Documentation (this feature)

```text
specs/013-historique-rdv-paiement/
├── plan.md
├── research.md
├── data-model.md
├── contracts/
│   └── rdv-historique-paiement.md
└── quickstart.md
```

### Source Code (repository root)

**Structure Decision**: Extension de la Clean Architecture existante (Option "Option 2" adaptée : pas
de dossier `backend/`/`frontend/` séparé, le projet utilise déjà `src/KekeBeauty.{Domain,Application,
Infrastructure,Api}` + `src/KekeBeauty.Web`).

```text
src/KekeBeauty.Application/Rdv/
├── IRdvRepository.cs                 # + ListByClientAsync, + IRdvPaiementRepository (meme fichier ou nouveau)
├── IRdvPaiementRepository.cs         # nouveau : CreerAsync/ObtenirParRdvAsync/MarquerPaiementAsync
├── InitierPaiementRdvUseCase.cs      # nouveau
└── VerifyRdvPaiementUseCase.cs       # nouveau (reconciliation manuelle, meme pattern que 008)

src/KekeBeauty.Infrastructure/Rdv/
└── RdvPaiementRepository.cs          # nouveau (Dapper, classe mutable)

src/KekeBeauty.Api/Controllers/
├── RdvController.cs                  # + GET /rdv (historique), + payerEnLigne sur POST /rdv,
│                                      #   + POST /rdv/{id}/paiement/verifier
└── WebhookController.cs              # callback essaie IAbonnementRepository puis IRdvPaiementRepository

db/migrations/0008_transaction_rdv.sql

src/KekeBeauty.Web/Components/Pages/
├── MesRendezVous.razor               # nouveau (US1)
└── PrendreRdv.razor                  # + choix paiement en ligne / sur place, + affichage lien WinPayer
```

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

Aucune violation — toutes les cases du Constitution Check passent sans justification supplémentaire.
