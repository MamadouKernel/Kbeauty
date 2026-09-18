# Implementation Plan: Inscription et Validation KYC des Établissements Partenaires

**Branch**: `004-onboarding-partenaire` | **Date**: 2026-09-18 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/004-onboarding-partenaire/spec.md`

## Summary

Ajouter un flux de soumission de dossier partenaire (compte + établissement + upload photo/pièce
d'identité, stockage local via volume Docker) et un flux d'administration (liste des dossiers en
attente, consultation des pièces, validation/rejet) protégé par une clé d'accès minimale en attendant
une véritable authentification administrateur (non encore développée dans le projet).

## Technical Context

**Language/Version**: C# 12 / .NET 8 (cohérent avec les features précédentes)

**Primary Dependencies**: Dapper/Npgsql (déjà en place) ; `IFormFile` ASP.NET Core pour l'upload
multipart ; aucune nouvelle dépendance externe

**Storage**: PostgreSQL `kekebeautyDb` (table `etablissement` déjà existante) + stockage fichier local
sur un volume Docker dédié (`/app/storage/kyc/`), les colonnes `url_photo_devanture`/`url_piece_identite`
stockant un chemin relatif, pas une URL publique

**Testing**: Tests manuels via `quickstart.md`

**Target Platform**: Conteneur `kekebeauty-api` existant

**Project Type**: Extension de l'API backend existante

**Performance Goals**: N/A (hors périmètre)

**Constraints**: La pièce d'identité ne MUST jamais être accessible sans passer par un contrôle
d'accès administrateur (FR-011) ; le stockage de fichiers doit rester remplaçable par un service dédié
sans changer le contrat d'API (spec, Assumptions)

**Scale/Scope**: 1 endpoint de soumission (multipart), 4 endpoints d'administration, 1 table de
référentiel géographique minimale (dépendance découverte, voir Research Décision 3)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principe | Vérification | Statut |
|---|---|---|
| I. Spec-Driven Development | Suit constitution → specify → plan (en cours) → tasks → implement | PASS |
| II. MERISE (NON-NEGOTIABLE) | Réutilise `etablissement`/`utilisateur` tels que modélisés ; ajout d'un référentiel géographique minimal, déjà prévu par le MCD/MLD (pays/région/ville/commune), pas une redéfinition | PASS |
| III. PostgreSQL conteneurisé | Migration versionnée `0004_geo_seed_minimal.sql` | PASS |
| IV. Gouvernance Scrum | Correspond à US-04/US-05 du Product Backlog | PASS |
| V. Sécurité des données sensibles | Pièce d'identité jamais exposée aux clients/partenaires (FR-011) ; endpoints admin protégés par clé (voir Décision 4) ; fichiers hors du DocumentRoot public | PASS |

**Complexité additionnelle justifiée** (voir Complexity Tracking) : ajout d'une protection minimale
par clé d'API pour les endpoints admin, alors qu'aucune authentification admin n'existe encore dans
le projet.

## Project Structure

### Documentation (this feature)

```text
specs/004-onboarding-partenaire/
├── plan.md
├── research.md
├── data-model.md
├── contracts/
├── quickstart.md
└── tasks.md
```

### Source Code (repository root)

```text
src/KekeBeauty.Application/
└── Onboarding/
    ├── IEtablissementRepository.cs
    ├── IFileStorage.cs
    ├── IPartnerNotifier.cs
    ├── SubmitPartnerApplicationUseCase.cs
    ├── ListPendingApplicationsUseCase.cs
    ├── ValidateApplicationUseCase.cs
    ├── RejectApplicationUseCase.cs
    └── Dtos.cs

src/KekeBeauty.Infrastructure/
└── Onboarding/
    ├── EtablissementRepository.cs      # Dapper
    ├── LocalFileStorage.cs             # Ecrit sur le volume /app/storage/kyc/
    └── ZavuWhatsAppPartnerNotifier.cs  # Notification rejet (memes garanties FR-009 que 003)

src/KekeBeauty.Api/
└── Controllers/
    ├── PartnersController.cs   # POST /partners/applications (multipart)
    └── AdminController.cs      # GET/POST /admin/applications/... (proteges par cle admin)

db/migrations/
└── 0004_geo_seed_minimal.sql   # Referentiel geographique minimal (dependance decouverte)

docker-compose.yml               # Ajout d'un volume nomme pour /app/storage sur le service api
```

**Structure Decision**: Même convention que `002`/`003` : entités existantes réutilisées, nouvelles
interfaces/cas d'usage dans `Application/Onboarding/`, implémentations dans `Infrastructure/Onboarding/`,
contrôleurs dans `Api/Controllers/`. Aucune couche existante modifiée hors ajout du volume de stockage
dans `docker-compose.yml`.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Protection par clé d'API statique sur les endpoints admin (pas une vraie authentification/rôle) | FR-011 interdit d'exposer la pièce d'identité sans contrôle d'accès, mais aucune feature d'authentification administrateur n'existe encore dans le projet (hors périmètre du Product Backlog actuel) | Laisser les endpoints admin ouverts violerait directement le Principe V de la constitution et FR-011 ; construire une authentification/RBAC complète maintenant serait disproportionné (YAGNI) pour une seule feature — à remplacer par une vraie feature d'authentification admin ultérieure |
| Migration de données de référence géographique (`0004_geo_seed_minimal.sql`) | La table `etablissement` a une clé étrangère `id_commune` NOT NULL, mais aucune donnée géographique n'a encore été créée (US-10, back-office référentiels, n'est pas encore implémentée) | Rendre `id_commune` nullable irait à l'encontre du MPD déjà validé (Principe II, non-négociable) ; sans données géographiques minimales, aucune insertion d'établissement n'est possible, ce qui bloquerait entièrement cette feature |
