# Implementation Plan: Frontend client — annuaire et prise de RDV

**Branch**: `010-frontend-client-annuaire-rdv` | **Date**: 2026-09-18 | **Spec**: [spec.md](./spec.md)

## Summary
Nouveau projet Blazor Web App (.NET 8, interactivité Server) consommant l'API `KekeBeauty.Api`
existante via `HttpClient`. Parcours client uniquement : recherche annuaire, fiche établissement,
authentification OTP, prise de RDV avec créneaux occupés visibles. Un petit ajout backend
(`GET /rdv/{id}`) est nécessaire pour rafraîchir le statut d'un RDV déjà créé (US3 AC3), l'API
actuelle n'offrant que la création (`POST /rdv`).

## Technical Context
**Language**: C# 12/.NET 8, Blazor Web App (render mode Server). **Storage**: aucun côté frontend —
consomme uniquement l'API existante. **Session**: `idUtilisateur` client conservé en `ProtectedLocalStorage`
(Blazor Server, cohérent avec FR-005 — pas de vraie session/JWT, dette déjà documentée côté API).

## Constitution Check
| Principe | Statut |
|---|---|
| I/II/IV | PASS — flux Spec Kit, correspond aux US déjà specifiées côté API (003/005/007) |
| II (MERISE) | PASS — aucune nouvelle entité, le frontend consomme l'API existante |
| III | PASS — pas de nouvelle table ; `GET /rdv/{id}` lit `rdv` existant |
| V | PASS — appel API via `HttpClient` interne au réseau Docker, pas de secret exposé côté navigateur |

## Project Structure
```text
src/KekeBeauty.Web/                          # nouveau projet Blazor Web App
├── Program.cs                               # DI : HttpClient nomme "Api" -> Api__BaseUrl
├── Services/
│   ├── DirectoryApiClient.cs                # GET /etablissements, GET /etablissements/{id}
│   ├── AuthApiClient.cs                     # POST /auth/otp/request|verify
│   ├── RdvApiClient.cs                      # GET /etablissements/{id}/creneaux, POST /rdv, GET /rdv/{id}
│   └── ClientSessionService.cs              # lecture/ecriture idUtilisateur en ProtectedLocalStorage
├── Components/Pages/
│   ├── Home.razor                           # recherche (categorie/commune)
│   ├── EtablissementDetail.razor            # fiche (medias, prestations, tel:, itineraire)
│   ├── Login.razor                          # OTP request/verify
│   └── PrendreRdv.razor                     # creneaux occupes + soumission demande + statut
└── wwwroot/

# Ajout backend minimal (API existante) :
src/KekeBeauty.Application/Rdv/IRdvRepository.cs       # + GetStatutAsync(idRdv, idClient)
src/KekeBeauty.Infrastructure/Rdv/RdvRepository.cs     # + implementation
src/KekeBeauty.Api/Controllers/RdvController.cs        # + GET rdv/{id} (verifie X-Client-Id proprietaire)
docker-compose.yml                                     # + service web (port dedie)
```

## Complexity Tracking
Aucune dérogation — nouveau projet frontend isolé, ajout backend minimal et justifié (US3 AC3 ne
peut pas être satisfait sans un moyen de relire le statut d'un RDV existant).
