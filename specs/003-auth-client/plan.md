# Implementation Plan: Inscription et Authentification Client par Téléphone

**Branch**: `003-auth-client` | **Date**: 2026-09-18 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/003-auth-client/spec.md`

## Summary

Ajouter à l'API existante un flux d'authentification sans mot de passe : demande d'un code OTP par
numéro de téléphone, envoi via WhatsApp (Zavu), validation du code, création ou récupération d'un
compte `Utilisateur` de type CLIENT. L'envoi réel dépend de la configuration Zavu (en cours) ; le
reste du flux (génération, stockage haché, expiration, unicité de compte) est développable et
testable dès maintenant, l'échec d'envoi Zavu produisant une réponse d'échec explicite (FR-009).

## Technical Context

**Language/Version**: C# 12 / .NET 8 (cohérent avec `002-scaffold-dotnet`)

**Primary Dependencies**: Dapper/Npgsql (déjà en place), `HttpClient` nommé pour l'appel à l'API Zavu
(pas de SDK Zavu .NET officiel connu — appel HTTP direct à l'API REST Zavu)

**Storage**: PostgreSQL `kekebeautyDb` — nouvelle table technique `otp_challenge` + table `utilisateur`
déjà existante (aucune modification de structure de `utilisateur`)

**Testing**: Tests manuels via `quickstart.md` (aucun test automatisé explicitement demandé par la spec)

**Target Platform**: Même conteneur `kekebeauty-api` que la feature 002

**Project Type**: Extension de l'API backend existante (pas de nouveau projet)

**Performance Goals**: N/A (hors périmètre de la spec)

**Constraints**: Le code OTP ne MUST jamais être stocké ni journalisé en clair (FR-010) ; l'envoi
réel est une dépendance externe (Zavu) non garantie disponible immédiatement (spec, Assumptions)

**Scale/Scope**: 2 nouveaux endpoints (`/auth/otp/request`, `/auth/otp/verify`), 1 nouvelle table

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principe | Vérification | Statut |
|---|---|---|
| I. Spec-Driven Development | Suit constitution → specify → plan (en cours) → tasks → implement | PASS |
| II. MERISE (NON-NEGOTIABLE) | Réutilise `utilisateur` tel que modélisé ; `otp_challenge` est une table technique documentée en data-model.md, pas une entité métier MERISE | PASS |
| III. PostgreSQL conteneurisé | Nouvelle table via migration versionnée `db/migrations/0003_otp_challenge.sql` | PASS |
| IV. Gouvernance Scrum | Correspond à US-03 du Product Backlog | PASS |
| V. Sécurité des données sensibles | Code OTP haché en base (jamais en clair), jamais journalisé (FR-010) ; clé API Zavu lue depuis variable d'environnement, jamais commitée | PASS |

Aucune violation constatée.

## Project Structure

### Documentation (this feature)

```text
specs/003-auth-client/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
└── tasks.md
```

### Source Code (repository root)

```text
src/KekeBeauty.Application/
└── Auth/
    ├── IOtpSender.cs              # Interface d'envoi OTP (implémentation Zavu en Infrastructure)
    ├── IOtpChallengeRepository.cs # Interface de persistance des challenges OTP
    ├── IUtilisateurRepository.cs  # Interface de lecture/création Utilisateur (type CLIENT)
    ├── RequestOtpUseCase.cs       # Cas d'usage : générer + envoyer un code
    ├── VerifyOtpUseCase.cs        # Cas d'usage : valider un code + créer/récupérer le compte
    └── Dtos.cs                    # RequestOtpResult, VerifyOtpResult

src/KekeBeauty.Infrastructure/
└── Auth/
    ├── ZavuWhatsAppOtpSender.cs      # Implémentation IOtpSender via l'API HTTP Zavu
    ├── OtpChallengeRepository.cs     # Implémentation Dapper (table otp_challenge)
    └── UtilisateurRepository.cs      # Implémentation Dapper (table utilisateur)

src/KekeBeauty.Api/
└── Controllers/
    └── AuthController.cs   # POST /auth/otp/request, POST /auth/otp/verify

db/migrations/
└── 0003_otp_challenge.sql   # Nouvelle table technique otp_challenge
```

**Structure Decision**: Suit exactement la convention posée par `002-scaffold-dotnet`
(`specs/002-scaffold-dotnet/quickstart.md`, revue de structure) : entité existante réutilisée
(`Utilisateur`), nouvelles interfaces/cas d'usage dans `Application/Auth/`, implémentations dans
`Infrastructure/Auth/`, contrôleur dans `Api/Controllers/`. Aucune couche existante n'est modifiée.

## Complexity Tracking

Aucune violation de la Constitution Check — section non applicable.
