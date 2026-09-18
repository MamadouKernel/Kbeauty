---
description: "Task list template for feature implementation"
---

# Tasks: Gestion des Prestations et Catégories par le Partenaire

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

## Phase 1: Foundational

- [ ] T001 Généraliser `RequestOtpUseCase`/`VerifyOtpUseCase` (feature 003) : remplacer la constante
  `"CLIENT"` par un paramètre `TypeCompte typeCompte = TypeCompte.Client`
- [ ] T002 Mettre à jour `AuthController` : `RequestOtpRequest`/`VerifyOtpRequest` acceptent un champ
  optionnel `typeCompte` (défaut `Client`), propagé aux use cases
- [ ] T003 [P] Créer `src/KekeBeauty.Application/Partner/IPartnerPrestationRepository.cs` (Add/Update/Delete
  prestation, Assign/Remove catégorie, `GetOwnerIdAsync(idEtablissement)`)
- [ ] T004 Créer `src/KekeBeauty.Infrastructure/Partner/PartnerPrestationRepository.cs` (Dapper)
- [ ] T005 Créer `src/KekeBeauty.Api/Auth/PartnerOwnershipFilter.cs` : lit `X-Partner-Id`, compare à
  `GetOwnerIdAsync`, `403` si absent/différent (voir `research.md` Décision 2)

## Phase 2: US2 - Gestion des prestations (P1) 🎯 MVP

- [ ] T006 [US2] Créer `ManagePrestationsUseCase.cs` (Add/Update/Delete)
- [ ] T007 [US2] Créer `PartnerManagementController.cs` : `POST/PUT/DELETE
  /partenaire/etablissements/{id}/prestations[/{idPrestation}]`, protégé par `PartnerOwnershipFilter`
- [ ] T008 [US2] Enregistrer les services dans le DI (`Program.cs`)
- [ ] T009 [US2] Exécuter Scénarios 1-2 de `quickstart.md`
- [ ] T010 [US2] Exécuter Scénario 3 (`403` sur établissement non possédé)

## Phase 3: US3 - Gestion des catégories (P2)

- [ ] T011 [US3] Créer `ManageCategoriesUseCase.cs` (réutilise `ICategorieRepository`, feature 005)
- [ ] T012 [US3] Ajouter `POST/DELETE .../categories[/{idCategorie}]` au contrôleur
- [ ] T013 [US3] Exécuter Scénario 4 de `quickstart.md`

## Phase 4: Polish
- [ ] T014 [P] Mettre à jour `README.md` et `docs/scrum/product-backlog.md` (US-11 →
  `006-gestion-prestations`) + `docs/scrum/sprint-5.md`
