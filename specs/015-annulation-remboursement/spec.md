# Feature Specification: Annulation client & retry paiement (Kéké Protect)

**Feature Branch**: `015-annulation-remboursement`
**Created**: 2026-09-19 | **Status**: Draft

**Input**: "Parcours 2 (Résilience) : permettre au client de relancer un paiement en ligne echoue sans
recreer de RDV, et d'annuler lui-meme un RDV a venir avec suivi honnete du remboursement si un
paiement avait deja reussi (pas de remboursement automatique reel, aucune integration de
remboursement WinPayer n'existe - marquage en attente de traitement manuel)."

## User Scenarios & Testing

### User Story 1 - Relancer un paiement échoué (Priority: P1)
En tant que cliente dont le paiement en ligne a échoué, je veux relancer une tentative sans perdre
mon créneau, pour finaliser ma réservation.

**Independent Test**: Sur un RDV avec `transaction_rdv.statut_transaction = ECHOUEE`, appeler l'endpoint
de relance et vérifier qu'un nouveau lien de paiement est généré pour la même transaction.

**Acceptance Scenarios**:
1. **Given** une transaction ECHOUEE, **When** la cliente relance, **Then** le système appelle
   l'agrégateur et repasse la transaction à EN_COURS avec une nouvelle référence externe.
2. **Given** une transaction REUSSIE ou EN_COURS, **When** la cliente tente une relance, **Then** le
   système refuse (rien à relancer).

### User Story 2 - Annuler un rendez-vous à venir (Priority: P1)
En tant que cliente, je veux annuler un RDV que je ne peux plus honorer, pour libérer le créneau et
informer le salon.

**Acceptance Scenarios**:
1. **Given** un RDV au statut DEMANDE ou CONFIRME, à venir, **When** la cliente l'annule, **Then** le
   statut passe à ANNULE.
2. **Given** un RDV déjà TERMINE, REFUSE ou ANNULE, **When** la cliente tente de l'annuler, **Then**
   le système refuse.
3. **Given** un RDV annulé dont le paiement en ligne était REUSSIE, **When** l'annulation est
   effectuée, **Then** la transaction passe à REMBOURSEE (statut de suivi) et un message honnête
   indique un traitement manuel sous 48h — aucun virement automatique réel n'est déclenché (pas
   d'API de remboursement WinPayer intégrée).

### Edge Cases
- Annuler un RDV n'appartenant pas au client : refusé (404, pas de fuite d'info, cohérent avec le
  reste de l'API).
- Un RDV sans paiement en ligne annulé ne touche à aucune transaction (rien à rembourser).

## Requirements

- **FR-001**: Le système DOIT permettre de relancer une transaction ECHOUEE pour le RDV propriétaire.
- **FR-002**: Le système DOIT refuser la relance d'une transaction non-ECHOUEE.
- **FR-003**: Le système DOIT permettre à un client d'annuler son propre RDV tant qu'il est au statut
  DEMANDE ou CONFIRME.
- **FR-004**: Le système DOIT refuser l'annulation d'un RDV déjà TERMINE/REFUSE/ANNULE ou n'appartenant
  pas au client.
- **FR-005**: Si une transaction REUSSIE existe pour un RDV annulé, le système DOIT marquer cette
  transaction REMBOURSEE et NE DOIT PAS prétendre qu'un virement a été effectué automatiquement (pas
  d'intégration de remboursement réelle à ce stade).

## Success Criteria
- **SC-001**: Une cliente peut relancer un paiement échoué en un clic sans reperdre son créneau.
- **SC-002**: Une cliente peut annuler un RDV à venir en moins de 10 secondes.
- **SC-003**: 0% des annulations n'affichent un faux message de remboursement instantané.

## Assumptions
- Pas de fenêtre "annulation gratuite jusqu'à 4h" imposée dans ce périmètre : l'annulation reste
  possible tant que le RDV n'est pas encore TERMINE/REFUSE/ANNULE (le salon reste notifié via son
  tableau de RDV existant, qui affichera le nouveau statut ANNULE).
- Le remboursement réel (virement Wave/Orange Money) reste un processus manuel côté équipe Keke
  Beauty, hors périmètre technique de cette feature — cohérent avec l'absence d'API de remboursement
  WinPayer intégrée à ce jour.
