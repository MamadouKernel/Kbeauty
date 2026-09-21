# Tasks: Historique des rendez-vous client et paiement en ligne

**Input**: Design documents from `specs/013-historique-rdv-paiement/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

## Phase 1: Setup

- [x] T001 Créer la migration `db/migrations/0008_transaction_rdv.sql` (table `transaction_rdv` + index `idx_transaction_rdv_reference_externe`, contenu exact dans `specs/013-historique-rdv-paiement/data-model.md`)
- [x] T002 Appliquer la migration via `scripts/db/migrate.sh` et vérifier son enregistrement dans `schema_migrations`

## Phase 2: Foundational (bloquant pour toutes les user stories)

- [x] T003 [P] Créer `IRdvPaiementRepository` dans `src/KekeBeauty.Application/Rdv/IRdvPaiementRepository.cs` (méthodes : `CreerAsync(idRdv, montant, ct)`, `ObtenirParRdvAsync(idRdv, ct)`, `ObtenirParReferenceAsync(referenceExterne, ct)`, `EnregistrerReferenceAsync(idTransaction, referenceExterne, ct)`, `MarquerPaiementAsync(referenceExterne, reussi, operateurExterne, ct)` — mêmes conventions de nommage que `IAbonnementRepository`)
- [x] T004 [P] Ajouter `ListByClientAsync(idClient, ct)` à `IRdvRepository` (interface dans `src/KekeBeauty.Application/Rdv/`) retournant une classe mutable `RdvHistoriqueItem` (jamais de `record` positionnel — convention Dapper du projet) avec les champs `IdRdv, NomEtablissement, LibellePrestation, DateHeureDebut, StatutRdv, StatutPaiement`
- [x] T005 Implémenter `RdvPaiementRepository` (Dapper) dans `src/KekeBeauty.Infrastructure/Rdv/RdvPaiementRepository.cs`, en respectant l'idempotence par `UNIQUE(id_rdv)` (insertion `ON CONFLICT (id_rdv) DO NOTHING` puis relecture, comme pattern défensif)
- [x] T006 Implémenter `ListByClientAsync` dans `src/KekeBeauty.Infrastructure/Rdv/RdvRepository.cs` avec la requête jointe documentée dans `data-model.md`
- [x] T007 Enregistrer `IRdvPaiementRepository → RdvPaiementRepository` dans le conteneur DI (`src/KekeBeauty.Api/Program.cs`, à côté de l'enregistrement existant de `IAbonnementRepository`)

**Checkpoint** : à ce stade, la couche de données est prête ; les user stories peuvent être implémentées indépendamment.

## Phase 3: User Story 1 - Consulter mon historique de rendez-vous (Priority: P1) 🎯 MVP

**Goal**: Un client authentifié peut lister tous ses RDV, triés du plus récent au plus ancien, avec statut.

**Independent Test**: `GET /rdv` avec un `X-Client-Id` valide retourne la liste attendue ; testable sans aucune des tâches de paiement (Phase 4/5).

- [x] T008 [US1] Ajouter l'endpoint `GET /rdv` dans `src/KekeBeauty.Api/Controllers/RdvController.cs` (vérifie `X-Client-Id`, appelle `IRdvRepository.ListByClientAsync`, retourne `401` si en-tête absent/invalide, `200` avec liste vide sinon)
- [x] T009 [US1] Ajouter `RdvHistoriqueItem`/`RdvApi.GetHistoriqueAsync` dans `src/KekeBeauty.Web/Models/ApiDtos.cs` et `src/KekeBeauty.Web/Services/RdvApiClient.cs` (même pattern `(bool Success, List<T>)` que `DirectoryApiClient.SearchAsync`)
- [x] T010 [US1] Créer la page `src/KekeBeauty.Web/Components/Pages/MesRendezVous.razor` (`@page "/mes-rendez-vous"`, `@rendermode @(new InteractiveServerRenderMode(prerender: false))` car lit la session client dans `OnInitializedAsync` — cf. bug de prerendering documenté), design Tailwind fidèle à `docs/design/stitch/mes_rendez_vous_historique/code.html`, données réelles uniquement
- [x] T011 [US1] Câbler le lien "Rendez-vous" de `src/KekeBeauty.Web/Components/Layout/BottomNav.razor` vers `/mes-rendez-vous`
- [ ] T012 [US1] Test xUnit dans `tests/KekeBeauty.Api.Tests/` : `GET /rdv` sans en-tête → 401 ; avec en-tête valide et RDV existants → 200 triés décroissant ; avec en-tête valide sans RDV → 200 `[]` (**non fait** — aucune infra de test n'existe encore dans le projet, `tests/KekeBeauty.Api.Tests/` ne contient qu'un `UnitTest1.cs` vide ; validé à la place via `quickstart.md` §1/4 contre la stack Docker réelle, cohérent avec la pratique déjà en place pour 003-012)

**Checkpoint** : US1 livrable seule (MVP) — l'historique fonctionne indépendamment du paiement en ligne.

## Phase 4: User Story 2 - Payer en ligne au moment de la demande de RDV (Priority: P2)

**Goal**: Le client peut choisir de payer en ligne à la demande de RDV ; le statut de paiement se met à jour via webhook.

**Independent Test**: `POST /rdv` avec `payerEnLigne:true` renvoie un lien de paiement ; un callback WiniPayer simulé (signature valide) met à jour le statut, visible via `GET /rdv` (US1) et sur l'écran de confirmation.

- [x] T013 [US2] Créer `InitierPaiementRdvUseCase` dans `src/KekeBeauty.Application/Rdv/InitierPaiementRdvUseCase.cs` (appelle `IPaymentGateway.InitiateAsync`, persiste via `IRdvPaiementRepository`, gère `Success=false` sans bloquer la création du RDV — FR-004)
- [x] T014 [US2] Étendre `RequestRdvBody` et l'action `RequestRdv` dans `src/KekeBeauty.Api/Controllers/RdvController.cs` pour accepter `payerEnLigne` et invoquer `InitierPaiementRdvUseCase` quand `true`, en retournant le bloc `paiement` documenté dans `contracts/rdv-historique-paiement.md`
- [x] T015 [US2] Étendre `WebhookController.Callback` dans `src/KekeBeauty.Api/Controllers/WebhookController.cs` : si `IAbonnementRepository.MarquerPaiementAsync` renvoie `false`, essayer `IRdvPaiementRepository.MarquerPaiementAsync` avec la même référence (vérification de hash déjà faite une seule fois, inchangée)
- [x] T016 [US2] Étendre `RdvApiClient.RequestRdvAsync` dans `src/KekeBeauty.Web/Services/RdvApiClient.cs` pour transmettre `payerEnLigne` et lire le bloc `paiement` de la réponse
- [x] T017 [US2] Mettre à jour `src/KekeBeauty.Web/Components/Pages/PrendreRdv.razor` : ajouter le choix "Payer en ligne" / "Payer sur place" dans le récapitulatif ; si un lien de paiement est retourné, rediriger le client vers `lienPaiement` (nouvel onglet ou navigation directe) et afficher le statut de paiement courant après retour
- [ ] T018 [US2] Test xUnit : `POST /rdv` avec `payerEnLigne:true` et gateway configuré (mock) → réponse contient `paiement.lienPaiement` ; callback avec référence RDV valide → statut passe à REUSSIE sans toucher `statut_rdv` (FR-010) (**non fait**, même raison que T012 — validé à la place via `quickstart.md` §3/5/6 contre WinPayer TEST réel : lien de checkout réel obtenu, callback avec hash calculé accepté, `statutPaiement` passé à REUSSIE et `statut_rdv` resté DEMANDE, confirmé en base)

**Checkpoint** : US1 + US2 livrables ensemble — paiement en ligne optionnel fonctionnel de bout en bout.

## Phase 5: User Story 3 - Vérifier manuellement un paiement resté en attente (Priority: P3)

**Goal**: Débloquer un paiement resté "en attente" sans attendre le webhook.

**Independent Test**: Avec une transaction `EN_COURS` en base, `POST /rdv/{id}/paiement/verifier` interroge WinPayer et met à jour le statut local.

- [x] T019 [US3] Créer `VerifyRdvPaiementUseCase` dans `src/KekeBeauty.Application/Rdv/VerifyRdvPaiementUseCase.cs` (même structure que `VerifyAbonnementPaiementUseCase.cs`, appelle `IPaymentGateway.VerifyAsync` puis `IRdvPaiementRepository.MarquerPaiementAsync` si `EstTerminal`)
- [x] T020 [US3] Ajouter l'endpoint `POST /rdv/{id}/paiement/verifier` dans `src/KekeBeauty.Api/Controllers/RdvController.cs` (vérifie `X-Client-Id` = propriétaire du RDV, `404` si transaction introuvable, `503` si agrégateur non configuré)
- [x] T021 [US3] Ajouter le bouton "Vérifier mon paiement" dans `src/KekeBeauty.Web/Components/Pages/MesRendezVous.razor` (visible seulement si `statutPaiement == "EN_COURS"`)
- [ ] T022 [US3] Test xUnit : vérification manuelle sans agrégateur configuré → 503 ; avec agrégateur mock renvoyant succès → statut mis à jour à REUSSIE (**non fait**, même raison que T012 — validé à la place via `quickstart.md` §7/8 : idempotence confirmée (1 seule ligne `transaction_rdv` après double appel), 404 confirmé sur RDV inexistant)

**Checkpoint** : Les trois user stories sont livrables indépendamment, dans l'ordre de priorité.

## Phase 6: Polish & Cross-Cutting

- [x] T023 [P] Mettre à jour `docs/scrum/product-backlog.md` et le sprint courant dans `docs/scrum/` pour refléter la feature 013 livrée
- [x] T024 [P] Vérifier `dotnet build`/`dotnet test` complets (solution entière) et `docker compose build api web` sans erreur
- [x] T025 Exécuter le guide `specs/013-historique-rdv-paiement/quickstart.md` de bout en bout contre la stack Docker locale

## Dependencies & Execution Order

- Phase 1 (Setup) → Phase 2 (Foundational) → bloque toutes les user stories.
- US1 (Phase 3) ne dépend d'aucune autre user story — c'est le MVP.
- US2 (Phase 4) dépend de la Phase 2 uniquement (pas de US1) mais son écran de confirmation bénéficie d'US1 déjà livrée pour la cohérence UX ; peut techniquement être livrée en parallèle d'US1.
- US3 (Phase 5) dépend de la persistance créée en Phase 2 et de `InitierPaiementRdvUseCase` (US2, T013) pour avoir des transactions à vérifier — dépendance logique sur US2, pas seulement Foundational.
- Phase 6 (Polish) après toutes les user stories retenues pour ce cycle.

## Parallel Execution Examples

- Phase 2 : T003 et T004 sont `[P]` (fichiers d'interfaces distincts, aucune dépendance croisée) ; T005/T006/T007 doivent suivre car ils implémentent T003/T004.
- Phase 6 : T023 et T024 sont `[P]` (documentation vs. build, fichiers disjoints).

## Implementation Strategy

**MVP = User Story 1 seule** (Phase 1 + 2 + 3) : livre l'historique client, la fonctionnalité la plus
demandée et la plus simple, sans dépendance au paiement en ligne. US2 et US3 s'ajoutent ensuite de façon
incrémentale sans retoucher US1.
