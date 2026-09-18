# Implementation Plan: Gestion des Prestations et Catégories par le Partenaire

**Branch**: `006-gestion-prestations` | **Date**: 2026-09-18 | **Spec**: [spec.md](./spec.md)

## Summary
Généraliser l'OTP existant (003) au type PARTENAIRE, ajouter des endpoints CRUD prestations +
assignation/retrait catégorie, protégés par vérification de propriété (`id_utilisateur_gerant`)
via un en-tête `X-Partner-Id` (dette technique acceptée, pas de session/JWT construit à ce stade).

## Technical Context
**Language**: C# 12/.NET 8. **Dependencies**: Dapper/Npgsql (déjà en place), aucune nouvelle.
**Storage**: `etablissement`, `prestation`, `categorie`, `etablissement_categorie` (existantes).
**Constraints**: FR-004 — refus explicite sur tout établissement non possédé.

## Constitution Check
| Principe | Vérification | Statut |
|---|---|---|
| I/II/III/IV | Suit le flux Spec Kit ; entités MERISE réutilisées ; correspond à US-11 | PASS |
| V. Sécurité | Vérification de propriété avant toute mutation ; en-tête `X-Partner-Id` documenté comme dette technique temporaire (cf. Complexity Tracking) | PASS |

## Project Structure
```text
src/KekeBeauty.Application/Auth/          # RequestOtpUseCase/VerifyOtpUseCase generalises (TypeCompte parametrable)
src/KekeBeauty.Application/Partner/
├── IPartnerPrestationRepository.cs   # CRUD scope etablissement + verification proprietaire
├── ManagePrestationsUseCase.cs       # Add/Update/Delete avec check ownership
└── ManageCategoriesUseCase.cs        # Assign/Remove avec check ownership
src/KekeBeauty.Infrastructure/Partner/
└── PartnerPrestationRepository.cs
src/KekeBeauty.Api/
├── Auth/PartnerOwnershipFilter.cs    # verifie id_utilisateur_gerant == X-Partner-Id
└── Controllers/PartnerManagementController.cs
```

## Complexity Tracking
| Violation | Why Needed | Alternative Rejected Because |
|---|---|---|
| En-tête `X-Partner-Id` comme preuve d'identité (pas de session/JWT) | Aucune feature de session n'existe encore ; le projet a déjà accepté ce type de compromis (clé admin statique, feature 004/005) | Construire une session/JWT complète maintenant est disproportionné (YAGNI) pour cette seule feature — sera remplacé quand une vraie feature de session sera planifiée |
