# Tasks: Abonnement et Paiement

**Input**: `specs/008-abonnement-paiement/{plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md}`

## Phase 1: Fondations (bloquant)
- [ ] T001 Migration `db/migrations/0005_parametre_abonnement.sql` — table `parametre_abonnement (periodicite PK, montant)`, seed des deux périodicités avec un montant par défaut
- [ ] T002 [P] `src/KekeBeauty.Application/Billing/IPaymentGateway.cs` — `Task<(bool Succes, string? Reference)> InitiateAsync(CanalPaiement canal, decimal montant)`
- [ ] T003 [P] `src/KekeBeauty.Application/Billing/IAbonnementRepository.cs` — `CreerAvecTransactionAsync`, `GetOwnerIdAsync(idEtablissement)`, `ListerAsync(statut?)`, `GetByIdAsync`, `MarquerNotifieAsync`, `GetTarifAsync(periodicite)`, `SetTarifAsync(periodicite, montant)`, `ListerTarifsAsync`

## Phase 2: User Story 1 — Souscription et paiement (P1)
- [ ] T004 [US1] `src/KekeBeauty.Infrastructure/Billing/CinetPayGateway.cs` — HttpClient nommé, échec explicite (`Succes=false`) si `Billing:CinetPay:ApiKey` absent, sinon POST vers l'agrégateur
- [ ] T005 [US1] `src/KekeBeauty.Infrastructure/Billing/AbonnementRepository.cs` (classe mutable, pas de record — Dapper) — insertion atomique `WHERE NOT EXISTS (... statut='ACTIF')`, retourne un flag de conflit si déjà `ACTIF`
- [ ] T006 [US1] `src/KekeBeauty.Application/Billing/SubscribeUseCase.cs` — lit le tarif standard, appelle `IPaymentGateway`, crée abonnement (`ACTIF`/`IMPAYE`) + transaction (`REUSSIE`/`ECHOUEE`) via le repository
- [ ] T007 [US1] `POST /api/etablissements/{id}/abonnements` dans `BillingController.cs`, protégé par `PartnerOwnershipFilter` (existant) → `201`/`409`/`401`/`403`

**Checkpoint**: souscription testable en Docker (succès simulé impossible sans agrégateur réel, mais échec explicite + 409 + 403 vérifiables).

## Phase 3: User Story 2 — Consultation et relance admin (P1)
- [ ] T008 [US2] `src/KekeBeauty.Application/Billing/AdminListAbonnementsUseCase.cs`
- [ ] T009 [US2] `src/KekeBeauty.Application/Billing/RelanceUseCase.cs` — réutilise `IZavuWhatsAppPartnerNotifier` existant, refuse (`409`) si l'abonnement n'est pas `IMPAYE`
- [ ] T010 [US2] `GET /api/admin/abonnements?statut=`, `POST /api/admin/abonnements/{id}/relance` dans `BillingController.cs`, protégés par `AdminApiKeyFilter` (existant)

## Phase 4: User Story 3 — Ajustement du tarif (P2)
- [ ] T011 [US3] `src/KekeBeauty.Application/Billing/UpdateTarifUseCase.cs` — valide `montant > 0`
- [ ] T012 [US3] `GET /api/admin/tarifs`, `PUT /api/admin/tarifs/{periodicite}` dans `BillingController.cs`

## Phase 5: Intégration & Polish
- [ ] T013 DI wiring dans `Program.cs` (`IPaymentGateway`→`CinetPayGateway` HttpClient nommé "CinetPay", `IAbonnementRepository`→`AbonnementRepository`, les 4 use cases)
- [ ] T014 Tester tous les scénarios de `quickstart.md` contre le container Docker réel via curl
- [ ] T015 Corriger les lignes US-15..US-18 dans `docs/scrum/product-backlog.md` → `008-abonnement-paiement`
- [ ] T016 Créer `docs/scrum/sprint-7.md`
- [ ] T017 `dotnet build` final, mettre à jour `README.md`
