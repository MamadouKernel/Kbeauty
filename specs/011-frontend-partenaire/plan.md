# Implementation Plan: Frontend partenaire — gestion établissement et RDV

**Branch**: `011-frontend-partenaire` | **Date**: 2026-09-18 | **Spec**: [spec.md](./spec.md)

## Summary
Ajout au projet Blazor Web existant (`KekeBeauty.Web`, 010) du parcours gérant : authentification
OTP (PARTENAIRE), tableau de bord listant ses établissements, gestion des prestations/catégories,
traitement des demandes de RDV. Deux endpoints backend minimaux manquants sont ajoutés (aucun ne
liste actuellement "mes établissements" ni "les RDV d'un établissement").

## Technical Context
**Language**: C# 12/.NET 8, mêmes composants Blazor Server que 010. **Storage**: aucun nouveau côté
frontend. **Backend** : 2 méthodes de lecture ajoutées (`IPartnerPrestationRepository.
GetEtablissementsByGerantAsync`, `IRdvRepository.ListByEtablissementAsync`), aucune nouvelle table.

## Constitution Check
| Principe | Statut |
|---|---|
| I/II/IV | PASS — correspond aux US déjà spécifiées côté API (006/007) |
| II (MERISE) | PASS — aucune nouvelle entité, lecture des entités existantes |
| III | PASS — pas de nouvelle table |
| V | PASS — mêmes filtres d'autorisation existants (`X-Partner-Id`, `PartnerOwnershipFilter`) |

## Project Structure
```text
# Ajouts backend minimaux (API existante) :
src/KekeBeauty.Application/Partner/IPartnerPrestationRepository.cs   # + GetEtablissementsByGerantAsync
src/KekeBeauty.Infrastructure/Partner/PartnerPrestationRepository.cs # + implementation
src/KekeBeauty.Application/Rdv/IRdvRepository.cs                     # + ListByEtablissementAsync
src/KekeBeauty.Infrastructure/Rdv/RdvRepository.cs                   # + implementation
src/KekeBeauty.Api/Controllers/PartnerManagementController.cs        # + GET partenaire/etablissements (liste "mes etablissements")
src/KekeBeauty.Api/Controllers/RdvController.cs                      # + GET partenaire/etablissements/{id}/rdv

# Frontend (KekeBeauty.Web existant) :
Models/PartnerApiDtos.cs
Services/PartnerApiClient.cs           # etablissements/prestations/categories
Services/PartnerRdvApiClient.cs        # liste + decisions RDV
Components/Pages/PartnerLogin.razor
Components/Pages/PartnerDashboard.razor        # liste etablissements du gerant
Components/Pages/PartnerEtablissement.razor    # prestations + categories
Components/Pages/PartnerRdv.razor              # liste RDV + confirmer/refuser/reprogrammer
```

## Complexity Tracking
Aucune dérogation — extension minimale et justifiée de lecture (deux méthodes de liste), aucune
nouvelle entité ni redéfinition métier.
