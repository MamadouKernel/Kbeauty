# Implementation Plan: Frontend admin — KYC, modération et abonnements

**Branch**: `012-frontend-admin` | **Date**: 2026-09-18 | **Spec**: [spec.md](./spec.md)

## Summary
Ajout au projet Blazor Web existant (`KekeBeauty.Web`, 010/011) du parcours administrateur :
connexion par clé API, gestion des dossiers KYC, modération des comptes/établissements, suivi et
tarification des abonnements. Aucun ajout backend requis — l'intégralité de l'API admin nécessaire
existe déjà (`AdminController`, `ModerationController`, `BillingController`).

## Technical Context
**Language**: C# 12/.NET 8, mêmes composants Blazor Server que 010/011. **Storage**: aucun nouveau
côté frontend, aucune modification backend.

## Constitution Check
| Principe | Statut |
|---|---|
| I/II/IV | PASS — correspond aux US déjà spécifiées côté API (004/008/009) |
| II (MERISE) | PASS — aucune nouvelle entité |
| III | PASS — pas de nouvelle table, pas de migration |
| V | PASS — même filtre `AdminApiKeyFilter` existant, clé jamais loguée côté frontend |

## Project Structure
```text
# Frontend (KekeBeauty.Web existant), aucun ajout backend :
Models/AdminApiDtos.cs
Services/AdminSessionService.cs        # cle API en ProtectedLocalStorage
Services/AdminApiClient.cs             # KYC (list/detail/validate/reject) + moderation + billing admin
Components/Pages/AdminLogin.razor
Components/Pages/AdminDossiers.razor           # liste + filtre statut
Components/Pages/AdminDossierDetail.razor      # detail + valider/rejeter
Components/Pages/AdminModeration.razor         # suspendre/reactiver par id
Components/Pages/AdminAbonnements.razor        # liste + filtre + relance + tarifs
```

## Complexity Tracking
Aucune dérogation — frontend pur, aucune modification de l'API existante.
