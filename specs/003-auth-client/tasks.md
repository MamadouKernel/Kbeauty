---

description: "Task list template for feature implementation"
---

# Tasks: Inscription et Authentification Client par Téléphone

**Input**: Design documents from `/specs/003-auth-client/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Non explicitement demandés — `quickstart.md` sert de plan de validation manuelle.

**Organization**: Tâches groupées par User Story (US1, US2 = P1 ; US3 = P2).

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [ ] T001 Créer `db/migrations/0003_otp_challenge.sql` : type `otp_status_enum` (`PENDING`,
  `CONSUMED`, `INVALIDATED`), table `otp_challenge` (voir `data-model.md`), index
  `(telephone, type_compte, status)`
- [ ] T002 Appliquer la migration via `./scripts/db/migrate.sh` et vérifier la table

## Phase 2: Foundational (bloquant pour toutes les User Stories)

- [ ] T003 [P] Créer `src/KekeBeauty.Application/Auth/Dtos.cs` : `RequestOtpResult`,
  `VerifyOtpResult` (records)
- [ ] T004 [P] Créer `src/KekeBeauty.Application/Auth/IOtpSender.cs` (interface :
  `Task<bool> SendAsync(string telephone, string code, CancellationToken ct)`)
- [ ] T005 [P] Créer `src/KekeBeauty.Application/Auth/IOtpChallengeRepository.cs` (interface :
  créer challenge + invalider les précédents en une opération, récupérer challenge `PENDING` actif
  par téléphone, marquer `CONSUMED`)
- [ ] T006 [P] Créer `src/KekeBeauty.Application/Auth/IUtilisateurRepository.cs` (interface :
  trouver par téléphone+type CLIENT, créer si absent)
- [ ] T007 Créer `src/KekeBeauty.Infrastructure/Auth/OtpChallengeRepository.cs` (Dapper — voir
  `data-model.md` pour les transitions d'état)
- [ ] T008 Créer `src/KekeBeauty.Infrastructure/Auth/UtilisateurRepository.cs` (Dapper — `INSERT ...
  ON CONFLICT DO NOTHING` sur la contrainte `UNIQUE (telephone, type_compte)` pour appliquer FR-007
  sans erreur applicative)
- [ ] T009 Configurer dans `src/KekeBeauty.Api/appsettings.json` un placeholder `Zavu:ApiKey` (vide) +
  `Zavu:BaseUrl` (`https://api.zavu.dev`), lu depuis `ConnectionStrings`-like config (jamais de vraie
  clé commitée)

## Phase 3: User Story 1 - Première inscription par téléphone (Priority: P1) 🎯 MVP

**Goal**: `POST /auth/otp/request` puis `POST /auth/otp/verify` créent un compte CLIENT actif.

**Independent Test**: Scénario 1 de `quickstart.md`.

- [ ] T010 [US1] Créer `src/KekeBeauty.Infrastructure/Auth/ZavuWhatsAppOtpSender.cs` : `HttpClient`
  nommé vers l'API Zavu ; retourne `false` immédiatement si `Zavu:ApiKey` est vide (FR-009, Decision 5
  de `research.md`), sans appel réseau
- [ ] T011 [US1] Créer `src/KekeBeauty.Application/Auth/RequestOtpUseCase.cs` : valide le format du
  numéro (FR-008), génère un code à 6 chiffres, le hache (SHA-256), invalide les challenges `PENDING`
  précédents et en crée un nouveau (FR-006), appelle `IOtpSender`, retourne succès/échec explicite
- [ ] T012 [US1] Créer `src/KekeBeauty.Application/Auth/VerifyOtpUseCase.cs` : récupère le challenge
  `PENDING` actif, vérifie expiration + hash, marque `CONSUMED`, crée l'utilisateur CLIENT si absent
  (`isNewAccount = true`) ou récupère l'existant (`false`)
- [ ] T013 [US1] Créer `src/KekeBeauty.Api/Controllers/AuthController.cs` : `POST /auth/otp/request`
  et `POST /auth/otp/verify`, mappés selon `contracts/auth-api.md` (codes 202/400/502/200)
- [ ] T014 [US1] Enregistrer tous les services dans le DI (`Program.cs`) : `IOtpSender`,
  `IOtpChallengeRepository`, `IUtilisateurRepository`, `HttpClient` nommé pour Zavu
- [ ] T015 [US1] Exécuter le Scénario 1 de `quickstart.md` (sans Zavu configuré : confirmer le `502`
  explicite ; documenter que le `200`/`202` sera vérifié dès que Zavu sera configuré)

**Checkpoint**: US1 livrable et testable pour son comportement d'échec explicite dès maintenant ;
le succès de bout en bout reste conditionné à la configuration Zavu (dépendance externe documentée).

## Phase 4: User Story 2 - Reconnexion ultérieure (Priority: P1)

**Goal**: Un numéro déjà associé à un compte CLIENT se reconnecte sans créer de doublon.

**Independent Test**: Scénario 2 de `quickstart.md`.

- [ ] T016 [US2] Vérifier dans `VerifyOtpUseCase` (T012) que le cas "utilisateur existant" est bien
  couvert par un test manuel dédié (Scénario 2 de `quickstart.md`) — aucune nouvelle classe requise,
  la logique est déjà unifiée avec US1
- [ ] T017 [US2] Exécuter le Scénario 2 de `quickstart.md` et confirmer `isNewAccount: false` +
  absence de doublon en base

**Checkpoint**: US1 + US2 livrées.

## Phase 5: User Story 3 - Expiration et usage unique du code (Priority: P2)

**Goal**: Un code déjà consommé ou expiré est explicitement refusé.

**Independent Test**: Scénarios 3 et 4 de `quickstart.md`.

- [ ] T018 [US3] Exécuter le Scénario 3 de `quickstart.md` (code incorrect, puis réutilisation d'un
  code déjà consommé) et confirmer le `400` explicite dans les deux cas
- [ ] T019 [US3] Exécuter le Scénario 4 de `quickstart.md` (invalidation par une nouvelle demande,
  FR-006) et confirmer le `400` sur l'ancien code

**Checkpoint**: US1 + US2 + US3 livrées — flux complet testable indépendamment de Zavu, prêt à être
validé de bout en bout dès la configuration Zavu finalisée.

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T020 [P] Mettre à jour `README.md` (section API backend) avec les deux nouveaux endpoints et
  la note sur la dépendance Zavu
- [ ] T021 Mettre à jour `docs/scrum/product-backlog.md` (US-03 → référence `003-auth-client`) et
  créer/mettre à jour `docs/scrum/sprint-2.md`

## Dependencies & Execution Order

- Setup (Phase 1, T001-T002) : bloque tout.
- Foundational (Phase 2, T003-T009) : bloque les User Stories.
- User Story 1 (Phase 3) : dépend de la Phase 2. MVP.
- User Story 2 (Phase 4) : dépend de la Phase 3 (même code, tests supplémentaires).
- User Story 3 (Phase 5) : dépend de la Phase 3.
- Polish (Phase 6) : après toutes les User Stories.

## Parallel Example

```text
T003, T004, T005, T006 [P] peuvent être écrits en parallèle (interfaces, fichiers distincts)
T007 et T008 peuvent être écrits en parallèle une fois T005/T006 posées
```

## Implementation Strategy

**MVP first** : Phase 3 (US1) avant Phases 4-5. Le comportement d'échec explicite (Zavu non
configuré) est testable immédiatement ; le succès de bout en bout attend la configuration Zavu —
ce n'est pas un blocage pour merger et avancer sur les autres features du backlog en parallèle.
