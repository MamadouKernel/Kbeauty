# Feature Specification: Modération back-office

**Feature Branch**: `009-moderation-back-office`
**Created**: 2026-09-18
**Status**: Draft
**Input**: User description: "Modération back-office (US-19). Un administrateur peut suspendre ou réactiver un compte client ou un établissement partenaire. Un compte/établissement suspendu ne doit plus pouvoir utiliser les fonctionnalités principales (authentification/RDV pour un client, visibilité annuaire/RDV/abonnement pour un établissement)."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Suspension et réactivation par l'administrateur (Priority: P1)
Un administrateur suspend un compte client ou un établissement problématique, et peut le réactiver.

**Independent Test**: Suspendre un compte, constater son statut ; le réactiver, constater le retour à la normale.

**Acceptance Scenarios**:
1. **Given** un compte client actif, **When** l'administrateur le suspend, **Then** le compte est marqué suspendu.
2. **Given** un établissement actif, **When** l'administrateur le suspend, **Then** l'établissement est marqué suspendu.
3. **Given** un compte ou établissement suspendu, **When** l'administrateur le réactive, **Then** il redevient utilisable normalement.
4. **Given** un identifiant de compte/établissement inexistant, **When** l'administrateur tente une suspension, **Then** l'action échoue explicitement (`404`).

### User Story 2 - Effets de la suspension sur les fonctionnalités (Priority: P1)
Un compte ou établissement suspendu ne peut plus utiliser les fonctionnalités principales du système.

**Independent Test**: Suspendre un client, constater le refus d'authentification (OTP) ; suspendre un établissement, constater sa disparition de l'annuaire et le refus de nouvelles demandes de RDV/abonnement.

**Acceptance Scenarios**:
1. **Given** un compte client suspendu, **When** il demande un code OTP, **Then** la demande est refusée explicitement.
2. **Given** un établissement suspendu, **When** un client recherche dans l'annuaire, **Then** cet établissement n'apparaît plus (même traitement que `statut_kyc` non `VALIDE`).
3. **Given** un établissement suspendu, **When** un client tente une demande de RDV sur cet établissement, **Then** la demande est refusée.
4. **Given** un établissement suspendu, **When** son gérant tente de souscrire un abonnement, **Then** la souscription est refusée.

### Edge Cases
- Suspendre un compte déjà suspendu → opération idempotente (reste suspendu, pas d'erreur).
- Réactiver un établissement dont le KYC n'a jamais été validé → redevient dans l'état KYC précédent (la suspension ne modifie pas `statut_kyc`, c'est un état orthogonal).

## Requirements *(mandatory)*
- **FR-001**: Le système MUST permettre à un administrateur de suspendre un compte utilisateur (client ou partenaire) par son identifiant.
- **FR-002**: Le système MUST permettre à un administrateur de suspendre un établissement par son identifiant.
- **FR-003**: Le système MUST permettre à un administrateur de réactiver un compte ou établissement suspendu.
- **FR-004**: Une action de suspension/réactivation sur un identifiant inexistant MUST échouer explicitement (`404`).
- **FR-005**: Un compte client suspendu MUST voir toute demande d'authentification (OTP) refusée.
- **FR-006**: Un établissement suspendu MUST être exclu de la recherche et de la fiche annuaire (même traitement que non `VALIDE`).
- **FR-007**: Un établissement suspendu MUST voir toute nouvelle demande de RDV refusée.
- **FR-008**: Un établissement suspendu MUST voir toute nouvelle souscription d'abonnement refusée.
- **FR-009**: La suspension/réactivation MUST être idempotente (aucune erreur si l'état cible est déjà atteint).

### Key Entities
- **Utilisateur**, **Etablissement** : entités déjà modélisées ; chacune reçoit un nouvel attribut
  d'état de modération (`est_suspendu`), orthogonal à `statut_kyc` (établissement) et au processus
  d'authentification (client).

## Success Criteria *(mandatory)*
- **SC-001**: 100% des tentatives d'authentification par un compte client suspendu sont refusées.
- **SC-002**: 100% des établissements suspendus sont absents des résultats de recherche annuaire.
- **SC-003**: 100% des demandes de RDV et souscriptions d'abonnement sur un établissement suspendu sont refusées.
- **SC-004**: 0 erreur sur une suspension/réactivation répétée du même compte/établissement (idempotence).

## Assumptions
- La modération d'un établissement (`est_suspendu`) est un état distinct de `statut_kyc` : un
  établissement `VALIDE` peut être suspendu, et un établissement suspendu réactivé retrouve
  exactement son `statut_kyc` antérieur (aucune interaction entre les deux champs).
- Aucune notification n'est envoyée au client/gérant lors d'une suspension (hors périmètre du CDC
  pour cette feature ; pourra être ajoutée ultérieurement via Zavu si besoin).
- La protection des endpoints admin réutilise le mécanisme existant (`AdminApiKeyFilter`), même
  dette technique documentée que les features précédentes (pas de RBAC réel).
