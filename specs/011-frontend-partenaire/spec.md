# Feature Specification: Frontend partenaire — gestion établissement et RDV

**Feature Branch**: `011-frontend-partenaire`
**Created**: 2026-09-18
**Status**: Draft
**Input**: User description: "Reprendre la construction du frontend. Deuxième parcours : le gérant partenaire (authentification, gestion des prestations/catégories de son établissement, traitement des demandes de RDV), correspondant aux User Stories déjà implémentées côté API (US-04/US-05 onboarding déjà couvert par une autre feature, US-11 gestion des prestations, US-13 décisions RDV)."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Authentification du gérant (Priority: P1)
Un gérant s'authentifie par OTP WhatsApp (compte de type PARTENAIRE) pour accéder à son espace.

**Independent Test**: Demander un code OTP en tant que PARTENAIRE, le vérifier, arriver sur le tableau de bord.

**Acceptance Scenarios**:
1. **Given** un numéro de gérant déjà inscrit, **When** il demande et vérifie un code OTP, **Then** il accède à son tableau de bord listant son ou ses établissements.
2. **Given** un gérant authentifié, **When** il navigue vers son espace après un premier passage, **Then** sa session (identifiant conservé) le reconnaît sans nouvelle authentification.

### User Story 2 - Gestion des prestations et catégories (Priority: P1)
Un gérant ajoute, modifie, supprime des prestations et assigne/retire des catégories de son établissement.

**Independent Test**: Ajouter une prestation, la modifier, la supprimer ; assigner une catégorie.

**Acceptance Scenarios**:
1. **Given** le tableau de bord d'un établissement, **When** le gérant ajoute une prestation (libellé, tarif, durée), **Then** elle apparaît dans la liste.
2. **Given** une prestation existante, **When** le gérant la modifie ou la supprime, **Then** le changement est reflété immédiatement.
3. **Given** un établissement sans catégorie, **When** le gérant assigne une catégorie, **Then** elle apparaît associée à l'établissement.
4. **Given** un gérant tentant d'agir sur un établissement qui n'est pas le sien, **When** l'action est soumise, **Then** elle est refusée (l'API renvoie `403`, reflété par un message explicite).

### User Story 3 - Traitement des demandes de RDV (Priority: P1)
Un gérant consulte les demandes de RDV de son établissement et les confirme, refuse ou reprogramme.

**Independent Test**: Une demande de RDV existante apparaît dans la liste ; la confirmer change son statut visible.

**Acceptance Scenarios**:
1. **Given** des demandes de RDV sur son établissement, **When** le gérant ouvre l'onglet RDV, **Then** il voit la liste avec statut, date/heure et prestation.
2. **Given** une demande au statut "Demandé", **When** le gérant la confirme ou la refuse, **Then** son statut affiché change immédiatement.
3. **Given** une demande confirmée, **When** le gérant la reprogramme sur un nouveau créneau libre, **Then** la nouvelle date/heure s'affiche ; sur un créneau occupé, un message d'erreur explicite s'affiche.

### Edge Cases
- Établissement suspendu : reste géré normalement par le gérant (la suspension bloque les nouvelles actions clients, pas l'accès de gestion — cohérent avec 009, aucune restriction additionnelle côté gestion).
- Aucun établissement associé au gérant authentifié : message explicite ("aucun établissement"), pas d'écran vide ni d'erreur technique.
- API indisponible : message d'erreur explicite sur chaque section, jamais un écran blanc (FR-008 de 010, repris ici).

## Requirements *(mandatory)*
- **FR-001**: Le frontend MUST permettre à un gérant de s'authentifier par OTP (type de compte PARTENAIRE), en consommant `POST /auth/otp/request|verify` (généralisé en 006).
- **FR-002**: Le frontend MUST permettre à un gérant authentifié de retrouver le ou les établissements qu'il gère. *(Nécessite un nouvel endpoint minimal côté API — aucun n'existe pour lister les établissements d'un gérant ; voir plan.md.)*
- **FR-003**: Le frontend MUST permettre d'ajouter, modifier, supprimer une prestation, et d'assigner/retirer une catégorie, en consommant `PartnerManagementController` (existant, 006).
- **FR-004**: Le frontend MUST permettre de consulter les demandes de RDV d'un établissement avec leur statut. *(Nécessite un nouvel endpoint minimal côté API — aucun ne liste les RDV d'un établissement ; voir plan.md.)*
- **FR-005**: Le frontend MUST permettre de confirmer, refuser, ou reprogrammer une demande de RDV, en consommant `RdvController` (existant, 007).
- **FR-006**: Le frontend MUST afficher un message d'erreur explicite (jamais un écran vide) sur toute erreur API, y compris `403` (établissement non possédé).

### Key Entities
Aucune nouvelle entité métier : consomme les entités déjà modélisées (Etablissement, Prestation,
Categorie, Rdv). Deux nouveaux endpoints de lecture (liste établissements d'un gérant, liste RDV
d'un établissement) exposent ces mêmes entités sans les redéfinir.

## Success Criteria *(mandatory)*
- **SC-001**: Un gérant peut passer de "connexion" à "prestation ajoutée" sans quitter l'application.
- **SC-002**: 100% des demandes de RDV existantes d'un établissement apparaissent dans la liste du gérant.
- **SC-003**: 100% des erreurs API (4xx/5xx) affichent un message explicite, jamais une page blanche.

## Assumptions
- **Session gérant** : même mécanisme que le client (010) — identifiant conservé en
  `ProtectedLocalStorage` après OTP, cohérent avec `X-Partner-Id` côté API (pas de vraie session/JWT).
- **Abonnement/paiement** : hors périmètre de cette itération — la souscription et le suivi de
  l'abonnement via l'interface partenaire feront l'objet d'une itération frontend ultérieure
  dédiée (le backend, lui, est déjà complet et testé en réel, feature 008).
- **Design** : composants Blazor par défaut (Bootstrap du template), même choix que 010.
