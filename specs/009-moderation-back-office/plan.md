# Implementation Plan: Modération back-office

**Branch**: `009-moderation-back-office` | **Date**: 2026-09-18 | **Spec**: [spec.md](./spec.md)

## Summary
Un administrateur suspend/réactive un compte (client/partenaire) ou un établissement via un nouvel
attribut `est_suspendu`, orthogonal à `statut_kyc`. Les points d'entrée existants (OTP, annuaire,
RDV, abonnement) sont modifiés pour refuser explicitement l'action quand la cible est suspendue.

## Technical Context
**Language**: C# 12/.NET 8. **Storage**: nouvelle colonne `est_suspendu BOOLEAN NOT NULL DEFAULT FALSE`
sur `utilisateur` et `etablissement` (migration 0006). **Constraints**: FR-009 — idempotence
(suspendre un compte déjà suspendu ne doit pas échouer).

## Constitution Check
| Principe | Statut |
|---|---|
| I/II/IV | PASS — flux Spec Kit, correspond à US-19 |
| II (MERISE) | PASS — `est_suspendu` est un attribut ajouté aux entités `Utilisateur`/`Etablissement` déjà modélisées, pas une nouvelle entité |
| III | PASS — migration versionnée `0006_moderation.sql` |
| V | PASS — endpoints admin réutilisent `AdminApiKeyFilter` existant |

## Project Structure
```text
src/KekeBeauty.Application/Moderation/
├── IModerationRepository.cs     # SuspendreUtilisateurAsync/ReactiverUtilisateurAsync/
│                                   SuspendreEtablissementAsync/ReactiverEtablissementAsync
│                                   (idempotents, retournent false si id introuvable)
├── SuspendUtilisateurUseCase.cs
└── SuspendEtablissementUseCase.cs
src/KekeBeauty.Infrastructure/Moderation/
└── ModerationRepository.cs
src/KekeBeauty.Api/Controllers/ModerationController.cs
db/migrations/0006_moderation.sql

# Modifications de features existantes (verifications de suspension) :
src/KekeBeauty.Infrastructure/Auth/OtpChallengeRepository.cs ou UtilisateurRepository.cs (003) — refuse l'OTP si utilisateur suspendu
src/KekeBeauty.Infrastructure/Listing/DirectoryRepository.cs (005) — exclut les etablissements suspendus (WHERE est_suspendu = false)
src/KekeBeauty.Infrastructure/Rdv/RdvRepository.cs (007) — refuse la creation de RDV si etablissement suspendu
src/KekeBeauty.Infrastructure/Billing/AbonnementRepository.cs (008) — refuse la souscription si etablissement suspendu
```

## Complexity Tracking
Aucune dérogation nécessaire — extension d'entités existantes par un attribut simple, cohérent avec
le MCD/MLD (pas de redéfinition métier).
