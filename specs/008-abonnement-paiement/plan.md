# Implementation Plan: Abonnement et Paiement

**Branch**: `008-abonnement-paiement` | **Date**: 2026-09-18 | **Spec**: [spec.md](./spec.md)

## Summary
Souscription d'abonnement (mensuel/annuel) par le gérant propriétaire, tentative de paiement via un
agrégateur (échec explicite si non configuré, même pattern que Zavu), consultation/relance/ajustement
tarifaire par l'administrateur (réutilise `AdminApiKeyFilter`, 004).

## Technical Context
**Language**: C# 12/.NET 8. **Storage**: `abonnement`/`transaction` (existantes) + nouvelle table
`parametre_abonnement` (tarif standard par périodicité, FR-007 — pas une entité MERISE métier).
**Constraints**: FR-004 — un seul abonnement `ACTIF` par établissement, vérifié atomiquement.

## Constitution Check
| Principe | Statut |
|---|---|
| I/II/IV | PASS — flux Spec Kit, correspond à US-15 à US-18 |
| II (MERISE) | PASS — `parametre_abonnement` est une donnée de configuration technique, pas une redéfinition d'entité métier (justifié en Complexity Tracking) |
| III | PASS — migration versionnée `0005_parametre_abonnement.sql` |
| V | PASS — clé API agrégateur jamais commitée ; endpoints admin réutilisent la protection existante |

## Project Structure
```text
src/KekeBeauty.Application/Billing/
├── IPaymentGateway.cs           # InitiateAsync(canal, montant) -> succes/echec explicite
├── IAbonnementRepository.cs     # creer/lister/GetOwnerIdAsync/tarif standard
├── SubscribeUseCase.cs
├── AdminListAbonnementsUseCase.cs
├── RelanceUseCase.cs
└── UpdateTarifUseCase.cs
src/KekeBeauty.Infrastructure/Billing/
├── AbonnementRepository.cs
└── CinetPayGateway.cs           # HttpClient, echec explicite si Billing:CinetPay:ApiKey absent
src/KekeBeauty.Api/Controllers/BillingController.cs
db/migrations/0005_parametre_abonnement.sql
```

## Complexity Tracking
| Violation | Why Needed | Alternative Rejected Because |
|---|---|---|
| Table `parametre_abonnement` hors MERISE | FR-007 exige un tarif standard modifiable par périodicité, non porté par `abonnement` (qui stocke le montant figé par souscription) | Stocker le tarif standard dans le code (constante) empêcherait l'admin de le modifier sans redéploiement, contraire à FR-007 |
