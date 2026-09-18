# Implementation Plan: Prise de Rendez-vous

**Branch**: `007-prise-rdv` | **Date**: 2026-09-18 | **Spec**: [spec.md](./spec.md)

## Summary
Endpoint public de consultation des créneaux occupés, endpoint client (identifié via `X-Client-Id`,
même compromis que `X-Partner-Id`) de demande de RDV avec détection de chevauchement, et endpoints
partenaire (validation/refus/reprogrammation) réutilisant `PartnerOwnershipFilter` (006) + notification
client via un nouveau `IRdvNotifier` (Zavu, même garantie d'échec explicite que 003/004).

## Technical Context
**Language**: C# 12/.NET 8. **Storage**: `rdv` (existante, aucune nouvelle table).
**Constraints**: FR-003/FR-007 — chevauchement détecté au niveau requête SQL (pas en mémoire) pour
éviter une course entre deux demandes simultanées (Edge Case).

## Constitution Check
| Principe | Statut |
|---|---|
| I/II/III/IV | PASS — suit le flux Spec Kit, réutilise `rdv`/`etablissement`/`prestation` déjà modélisés |
| V. Sécurité | PASS — `X-Client-Id`/`X-Partner-Id` documentés comme dette technique déjà acceptée (006) |

## Project Structure
```text
src/KekeBeauty.Application/Rdv/
├── IRdvRepository.cs        # creneaux occupes, creation avec check chevauchement (requete atomique), decision
├── IRdvNotifier.cs
├── RequestRdvUseCase.cs
├── DecideRdvUseCase.cs      # confirmer/refuser/reprogrammer
└── Dtos.cs
src/KekeBeauty.Infrastructure/Rdv/
├── RdvRepository.cs         # Dapper ; INSERT ... WHERE NOT EXISTS (chevauchement) pour eviter la course
└── ZavuWhatsAppRdvNotifier.cs
src/KekeBeauty.Api/Controllers/RdvController.cs   # public (creneaux) + client (POST /rdv) + partenaire (decisions)
```

## Complexity Tracking
Aucune violation nouvelle — `X-Client-Id` suit exactement le précédent déjà accepté et documenté (006).
