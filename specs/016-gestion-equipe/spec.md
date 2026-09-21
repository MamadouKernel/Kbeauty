# Feature Specification: Gestion d'équipe (collaboratrices)

**Feature Branch**: `016-gestion-equipe`
**Created**: 2026-09-19 | **Status**: Draft

**Input**: "Parcours 5 (Staff), perimetre reduit et honnete : permettre au gerant partenaire de
referencer les collaboratrices de son salon (nom, specialite) pour affichage sur 'Mon Salon'. Les
comptes de connexion individuels pour les collaboratrices (planning personnel, fiches techniques,
commissions/pourboires en self-service) restent HORS PERIMETRE de cette feature : ils necessiteraient
un nouveau type de compte, une session dediee et des ecrans dedies - un chantier distinct, pas une
simple extension."

## User Scenarios & Testing

### User Story 1 - Référencer les collaboratrices de mon salon (Priority: P1)
En tant que gérante partenaire, je veux ajouter les membres de mon équipe (nom, spécialité) pour que
mon salon affiche une équipe identifiée.

**Acceptance Scenarios**:
1. **Given** un établissement du gérant, **When** il ajoute une collaboratrice (nom, spécialité
   optionnelle), **Then** elle apparaît dans la liste de l'équipe de cet établissement.
2. **Given** une collaboratrice existante, **When** le gérant la retire, **Then** elle disparaît de
   la liste.
3. **Given** un établissement n'appartenant pas au gérant, **When** il tente d'y ajouter une
   collaboratrice, **Then** le système refuse (403, cohérent avec `PartnerOwnershipFilter` existant).

### Edge Cases
- Aucune limite de nombre de collaboratrices.
- Aucune suppression en cascade nécessaire : pas de FK entrante sur `collaborateur` dans ce périmètre
  (pas d'assignation de RDV à une collaboratrice — hors scope, nécessiterait de revoir le flux de
  réservation client).

## Requirements
- **FR-001**: Le système DOIT permettre au gérant propriétaire d'un établissement d'ajouter une
  collaboratrice (nom obligatoire, spécialité optionnelle).
- **FR-002**: Le système DOIT permettre de lister les collaboratrices d'un établissement.
- **FR-003**: Le système DOIT permettre au gérant propriétaire de retirer une collaboratrice.
- **FR-004**: Le système NE DOIT PAS exposer de mécanisme de connexion pour les collaboratrices dans
  ce périmètre (pas de compte, pas de session) — évite de construire un demi-système d'authentification
  non fonctionnel.

## Success Criteria
- **SC-001**: Un gérant peut construire la liste de son équipe (3 personnes) en moins d'une minute.
- **SC-002**: 0% des écrans ne prétendent offrir un planning individuel ou des commissions
  fonctionnels tant que le compte collaboratrice n'existe pas.

## Assumptions
- Le planning individuel, les fiches techniques clientes et les commissions/pourboires par
  collaboratrice (US "Planning Personnel", "Fiche Technique Cliente", "Mes Commissions" du Parcours 5)
  restent un chantier séparé, car ils supposent une identité de connexion propre à la collaboratrice —
  généraliser l'OTP à un 3e `TypeCompte` (aujourd'hui CLIENT/PARTENAIRE) est un changement structurant
  qui dépasse le cadre "ajouter une info d'équipe" et mérite son propre cycle spec→plan→tasks.
