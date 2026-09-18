# Feature Specification: Abonnement et Paiement

**Feature Branch**: `008-abonnement-paiement`
**Created**: 2026-09-18
**Status**: Draft
**Input**: User description: "Abonnement et paiement (US-15 à US-18). Un partenaire souscrit un abonnement (mensuel/annuel, engagement 1 an min) et le paie (Mobile Money ou carte, via un agrégateur externe). Un administrateur consulte les abonnements, relance les impayés, et ajuste le tarif des abonnements."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Souscription et paiement (Priority: P1)
Un gérant, propriétaire d'un établissement, souscrit un abonnement (mensuel ou annuel) et le paie.

**Independent Test**: Souscrire avec un canal de paiement, constater la création de l'abonnement et de la transaction.

**Acceptance Scenarios**:
1. **Given** un gérant propriétaire, **When** il souscrit avec un canal de paiement et que le paiement réussit, **Then** l'abonnement est `ACTIF` et la transaction `REUSSIE`.
2. **Given** un paiement qui échoue (agrégateur non configuré ou refus), **When** la souscription est tentée, **Then** l'abonnement est créé `IMPAYE` et la transaction `ECHOUEE`, sans blocage silencieux.
3. **Given** un établissement qui n'est pas le sien, **When** un gérant tente de souscrire pour cet établissement, **Then** l'action est refusée (`403`).

### User Story 2 - Consultation et relance par l'administrateur (Priority: P1)
Un administrateur consulte les abonnements (notamment les impayés) et déclenche une relance.

**Independent Test**: Lister les abonnements filtrés par statut, déclencher une relance sur un impayé.

**Acceptance Scenarios**:
1. **Given** des abonnements de statuts variés, **When** un administrateur les liste par statut, **Then** il voit exactement ceux du statut demandé.
2. **Given** un abonnement `IMPAYE`, **When** l'administrateur déclenche une relance, **Then** une tentative de notification est envoyée au gérant.

### User Story 3 - Ajustement du tarif par l'administrateur (Priority: P2)
Un administrateur consulte et modifie le tarif standard des abonnements (mensuel/annuel).

**Independent Test**: Modifier le tarif d'une périodicité, constater qu'une nouvelle souscription l'utilise.

**Acceptance Scenarios**:
1. **Given** un tarif standard existant pour une périodicité, **When** un administrateur le modifie, **Then** toute nouvelle souscription pour cette périodicité utilise le nouveau montant (les abonnements déjà souscrits ne sont pas rétroactivement modifiés).

### Edge Cases
- Souscription alors qu'un abonnement `ACTIF` existe déjà pour le même établissement → refusée (un seul abonnement actif par établissement).
- Agrégateur de paiement non configuré → échec explicite (comme les autres intégrations Zavu déjà en place), pas un succès trompeur.

## Requirements *(mandatory)*
- **FR-001**: Le système MUST permettre à un gérant propriétaire de souscrire un abonnement (mensuel ou annuel) pour son établissement, avec un engagement minimal de 1 an quelle que soit la périodicité de facturation (RG-ABO-02/03).
- **FR-002**: Le système MUST tenter un paiement (Mobile Money ou carte) au moment de la souscription et refléter fidèlement son résultat (`ACTIF`/`REUSSIE` ou `IMPAYE`/`ECHOUEE`), jamais un succès trompeur.
- **FR-003**: Toute souscription sur un établissement non possédé par le gérant authentifié MUST être refusée (`403`).
- **FR-004**: Un établissement ne MUST avoir qu'un seul abonnement `ACTIF` à la fois.
- **FR-005**: Le système MUST permettre à un administrateur de lister les abonnements filtrés par statut (RG-ADM, US-17).
- **FR-006**: Le système MUST permettre à un administrateur de déclencher une relance (notification) sur un abonnement `IMPAYE`.
- **FR-007**: Le système MUST permettre à un administrateur de consulter et modifier le tarif standard par périodicité (RG-ABO-04) ; la modification MUST s'appliquer uniquement aux souscriptions futures.

### Key Entities
- **Abonnement**, **Transaction**, **Etablissement** : déjà modélisées, cycle de vie piloté par cette feature.
- **Tarif standard par périodicité** : nouvelle donnée de configuration (mensuel/annuel), pas une entité MERISE métier — nécessaire pour appliquer FR-007 sans redéfinir `Abonnement`.

## Success Criteria *(mandatory)*
- **SC-001**: 100% des résultats de paiement (succès/échec) sont reflétés fidèlement sur l'abonnement et la transaction.
- **SC-002**: 0 établissement avec deux abonnements `ACTIF` simultanés.
- **SC-003**: 100% des tentatives de souscription sur un établissement non possédé sont refusées.

## Assumptions
- **Mise à jour 2026-09-18** : l'agrégateur retenu est **WiniPayer** (compte marchand "Keke Beauty"
  créé en environnement TEST via https://manager.winipayer.com, remplace l'hypothèse initiale
  CinetPay/PaySika/TouchPay). WiniPayer fonctionne par lien de paiement hébergé (checkout) et
  notification asynchrone (callback), pas par paiement synchrone — voir research.md Décision 4.
  Le passage en environnement PROD nécessitera de renseigner `WINIPAYER_PROD_TOKEN_KEY`/
  `WINIPAYER_PROD_PRIVATE_KEY` et `WINIPAYER_ENV=prod`.
- RG-RDV-01 (module RDV conditionné à un abonnement actif) n'est pas rétrofitée dans la feature
  007 par cette itération — elle pourra être ajoutée dans un correctif ultérieur une fois l'abonnement
  disponible.
- Un seul canal de notification de relance (WhatsApp/Zavu, déjà en place) est utilisé.
