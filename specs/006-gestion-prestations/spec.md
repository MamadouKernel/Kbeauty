# Feature Specification: Gestion des Prestations et Catégories par le Partenaire

**Feature Branch**: `006-gestion-prestations`
**Created**: 2026-09-18
**Status**: Draft
**Input**: User description: "Gestion des prestations et catégories par le partenaire (US-11). Le gérant, une fois identifié par OTP WhatsApp (généralisé aux comptes PARTENAIRE), gère ses propres prestations (CRUD) et catégories (assigner/retirer), sans dépendre d'un administrateur. Un gérant ne peut jamais modifier un établissement qui n'est pas le sien."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Authentification du gérant (Priority: P1)
Le gérant s'identifie par téléphone + code OTP WhatsApp pour obtenir son identité PARTENAIRE.

**Why**: Prérequis à toute action de gestion.
**Independent Test**: Demander/valider un OTP pour un compte PARTENAIRE existant, obtenir son identifiant.

**Acceptance Scenarios**:
1. **Given** un compte PARTENAIRE existant, **When** le gérant demande puis valide un code OTP, **Then** il obtient son identifiant utilisateur PARTENAIRE (même mécanisme que pour un CLIENT, feature 003).
2. **Given** un numéro sans compte PARTENAIRE, **When** un OTP est validé, **Then** aucun accès de gestion n'est possible (pas d'établissement associé).

### User Story 2 - Gestion des prestations (Priority: P1)
Le gérant ajoute, modifie et supprime les prestations de son propre établissement.

**Why**: Cœur de la feature — remplace le mécanisme admin temporaire (005).
**Independent Test**: Créer/modifier/supprimer une prestation sur son établissement et vérifier le résultat sur la fiche publique (005).

**Acceptance Scenarios**:
1. **Given** un gérant identifié propriétaire d'un établissement, **When** il ajoute une prestation (libellé, tarif, durée), **Then** elle est créée et visible sur la fiche publique.
2. **Given** une prestation existante de son établissement, **When** il la modifie, **Then** les nouvelles valeurs sont reflétées.
3. **Given** une prestation existante, **When** il la supprime, **Then** elle disparaît de la fiche publique.
4. **Given** un établissement qui n'est pas le sien, **When** le gérant tente d'y ajouter/modifier/supprimer une prestation, **Then** l'action est refusée.

### User Story 3 - Gestion des catégories (Priority: P2)
Le gérant assigne ou retire une catégorie de son établissement.

**Why**: Complète US2 mais moins fréquent (catégories changent rarement).
**Independent Test**: Assigner puis retirer une catégorie, vérifier l'effet sur la recherche (005).

**Acceptance Scenarios**:
1. **Given** un gérant propriétaire, **When** il assigne une catégorie, **Then** son établissement devient trouvable par cette catégorie.
2. **Given** une catégorie déjà assignée, **When** il la retire, **Then** l'établissement n'est plus trouvable par cette catégorie (sauf s'il en a d'autres).

### Edge Cases
- Un gérant sans établissement (dossier jamais soumis) tente une action de gestion → refus explicite.
- Retirer la dernière catégorie d'un établissement → il devient invisible en recherche (cohérent avec FR-002 de la feature 005), sans erreur bloquante.

## Requirements *(mandatory)*
- **FR-001**: Le mécanisme OTP existant (feature 003) MUST être généralisé pour authentifier aussi bien un compte CLIENT qu'un compte PARTENAIRE, selon le type demandé.
- **FR-002**: Le système MUST permettre à un gérant identifié de créer, modifier et supprimer les prestations de son propre établissement uniquement.
- **FR-003**: Le système MUST permettre à un gérant identifié d'assigner ou retirer une catégorie de son propre établissement uniquement.
- **FR-004**: Toute tentative de gestion sur un établissement dont le gérant n'est pas le propriétaire MUST être refusée explicitement.
- **FR-005**: Un gérant sans établissement associé MUST recevoir un refus explicite sur toute action de gestion.

### Key Entities
- **Utilisateur (PARTENAIRE)**, **Établissement**, **Prestation**, **Categorie** : entités déjà modélisées, cette feature pilote leur cycle CRUD/association côté partenaire.

## Success Criteria *(mandatory)*
- **SC-001**: 100% des tentatives de gestion sur un établissement non possédé sont refusées (0 fuite constatée).
- **SC-002**: Une prestation ajoutée/modifiée/supprimée par le gérant se reflète immédiatement sur la fiche publique (005).

## Assumptions
- Aucune session/jeton persistant n'existe encore dans le projet (aucune feature d'authentification par session/JWT n'a été construite). L'identité obtenue après validation OTP (`idUtilisateur`) est donc utilisée comme identifiant porté par le client à chaque appel de gestion (ex. en-tête), de façon similaire au mécanisme déjà accepté pour la clé admin (dette technique documentée, à remplacer par une vraie gestion de session dans une feature future).
- Un établissement a un seul gérant (`id_utilisateur_gerant`, déjà modélisé) — pas de gestion multi-gérants dans cette feature.
