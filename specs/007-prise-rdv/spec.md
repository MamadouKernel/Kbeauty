# Feature Specification: Prise de Rendez-vous

**Feature Branch**: `007-prise-rdv`
**Created**: 2026-09-18
**Status**: Draft
**Input**: User description: "Prise de rendez-vous (US-12 à US-14). Un client identifié (OTP) demande un RDV pour une prestation d'un établissement validé, sur un créneau non déjà occupé. Le gérant valide/refuse/reprogramme la demande. Le client est notifié de la décision. Les créneaux déjà occupés doivent être visibles avant la demande."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Consultation des créneaux occupés (Priority: P1)
Un client consulte, pour un établissement et une date donnés, les créneaux déjà occupés avant de choisir une heure.

**Independent Test**: Consulter les créneaux d'un établissement ayant déjà un RDV confirmé/demandé ce jour-là.

**Acceptance Scenarios**:
1. **Given** un établissement avec un RDV `DEMANDE` ou `CONFIRME` à une heure donnée, **When** un client consulte les créneaux de ce jour, **Then** ce créneau apparaît comme occupé.
2. **Given** un RDV `REFUSE` ou `ANNULE`, **When** un client consulte les créneaux, **Then** ce créneau n'apparaît pas comme occupé.

### User Story 2 - Demande de rendez-vous (Priority: P1)
Un client identifié demande un RDV pour une prestation d'un établissement validé, sur un créneau libre.

**Independent Test**: Demander un RDV sur un créneau libre et constater sa création au statut `DEMANDE`.

**Acceptance Scenarios**:
1. **Given** un établissement validé, une prestation existante et un créneau libre, **When** un client demande un RDV, **Then** il est créé au statut `DEMANDE`.
2. **Given** un créneau déjà occupé (chevauchement avec un RDV `DEMANDE`/`CONFIRME` existant), **When** un client tente une demande sur ce créneau, **Then** la demande est refusée avant création.
3. **Given** un établissement non validé ou une prestation inexistante, **When** un client tente une demande, **Then** elle est refusée.

### User Story 3 - Décision du gérant et notification (Priority: P1)
Le gérant valide, refuse ou reprogramme une demande de RDV sur son établissement ; le client est notifié.

**Independent Test**: Décider d'une demande `DEMANDE` existante et vérifier le changement de statut + la tentative de notification.

**Acceptance Scenarios**:
1. **Given** une demande `DEMANDE` sur son établissement, **When** le gérant la valide, **Then** le statut passe à `CONFIRME` et le client est notifié.
2. **Given** une demande `DEMANDE`, **When** le gérant la refuse, **Then** le statut passe à `REFUSE` et le client est notifié.
3. **Given** une demande `DEMANDE`, **When** le gérant la reprogramme (nouvelle date/heure), **Then** le créneau est mis à jour, le statut reste `DEMANDE`, et le client est notifié du changement proposé.
4. **Given** une demande sur un établissement qui n'est pas le sien, **When** un gérant tente une décision, **Then** elle est refusée (`403`).

### Edge Cases
- Deux demandes simultanées sur le même créneau libre → une seule doit réussir, l'autre doit être refusée (pas de double réservation).
- Reprogrammation vers un créneau déjà occupé par un autre RDV → refusée.

## Requirements *(mandatory)*
- **FR-001**: Le système MUST exposer les créneaux occupés (`DEMANDE`/`CONFIRME`) d'un établissement pour une date donnée.
- **FR-002**: Le système MUST permettre à un client identifié de demander un RDV sur un établissement validé, une prestation existante, et un créneau libre.
- **FR-003**: Toute demande sur un créneau chevauchant un RDV `DEMANDE`/`CONFIRME` existant MUST être refusée avant création (RG-RDV-02).
- **FR-004**: Le système MUST permettre au gérant propriétaire de l'établissement de valider, refuser ou reprogrammer une demande `DEMANDE` (RG-RDV-04).
- **FR-005**: Toute décision sur un établissement non possédé par le gérant authentifié MUST être refusée (`403`).
- **FR-006**: Le système MUST notifier le client de toute décision (validation/refus/reprogrammation) (RG-RDV-03).
- **FR-007**: Une reprogrammation vers un créneau déjà occupé par un autre RDV MUST être refusée.

### Key Entities
- **Rdv**, **Etablissement**, **Prestation**, **Utilisateur** : entités déjà modélisées ; cette feature pilote le cycle de vie de `Rdv` (`statut_rdv`).

## Success Criteria *(mandatory)*
- **SC-001**: 0 double réservation constatée sur un même créneau (chevauchement toujours détecté).
- **SC-002**: 100% des décisions déclenchent une tentative de notification client.
- **SC-003**: 100% des décisions sur un établissement non possédé sont refusées.

## Assumptions
- Le module de réservation n'est pas encore conditionné à un abonnement actif (feature d'abonnement/paiement non encore construite) — RG-RDV-01 sera appliquée dans une feature future dédiée à l'abonnement ; cette feature suppose l'accès ouvert.
- La notification réutilise le canal WhatsApp/Zavu déjà en place (même comportement d'échec explicite si Zavu non configuré).
- Aucune génération de grille de créneaux à partir des horaires n'est dans le périmètre ; seule la détection de chevauchement avec les RDV existants est couverte.
