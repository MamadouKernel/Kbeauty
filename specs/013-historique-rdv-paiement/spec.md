# Feature Specification: Historique des rendez-vous client et paiement en ligne

**Feature Branch**: `013-historique-rdv-paiement`

**Created**: 2026-09-19

**Status**: Draft

**Input**: User description: "Historique des rendez-vous client et paiement en ligne a la reservation (feature next du Product Backlog, Parcours 1 client). Le systeme doit permettre a un client deja authentifie (compte CLIENT, mecanisme OTP existant depuis la feature 003-auth-client) de consulter la liste de tous ses rendez-vous passes et a venir, tries du plus recent au plus ancien, avec pour chacun : l'etablissement, la prestation, la date/heure, et le statut (DEMANDE, CONFIRME, REFUSE, ANNULE, TERMINE). Le systeme doit egalement permettre, au moment de la demande de rendez-vous, de proposer un paiement en ligne du montant de la prestation via Mobile Money (Orange Money, MTN, Moov, Wave) en utilisant l'agregateur de paiement WinPayer deja integre au projet pour les abonnements partenaires (checkout hosted + callback webhook + verification manuelle). Le paiement est optionnel a la demande de RDV : un client peut aussi choisir de payer sur place au salon. Si le client choisit le paiement en ligne, le systeme cree une transaction de paiement liee au rendez-vous, redirige le client vers le lien de paiement hosted WinPayer, et met a jour le statut du paiement via le webhook existant. Le client doit pouvoir consulter le statut de son paiement (en attente, paye, echoue) sur l'ecran de confirmation et dans son historique de rendez-vous."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Consulter mon historique de rendez-vous (Priority: P1)

En tant que cliente déjà connectée, je veux voir la liste de tous mes rendez-vous (passés et à venir) avec leur statut, pour suivre mes réservations sans avoir à contacter chaque salon individuellement.

**Why this priority**: C'est la fonctionnalité la plus attendue et la plus simple à livrer seule ; elle ne dépend pas du paiement en ligne et apporte déjà de la valeur (visibilité) à toute cliente ayant déjà pris au moins un RDV.

**Independent Test**: Se connecter avec un compte client ayant au moins un RDV existant, ouvrir l'écran "Mes rendez-vous", et vérifier que la liste affiche l'établissement, la prestation, la date/heure et le statut de chaque RDV, triée du plus récent au plus ancien.

**Acceptance Scenarios**:

1. **Given** une cliente connectée ayant 3 RDV (un DEMANDE, un CONFIRME, un TERMINE), **When** elle ouvre "Mes rendez-vous", **Then** les 3 RDV s'affichent triés du plus récent au plus ancien avec leur statut respectif.
2. **Given** une cliente connectée n'ayant jamais pris de RDV, **When** elle ouvre "Mes rendez-vous", **Then** un message clair indique l'absence de rendez-vous (pas d'erreur, pas de liste vide silencieuse).
3. **Given** une cliente non connectée, **When** elle tente d'accéder à "Mes rendez-vous", **Then** elle est redirigée vers la connexion puis ramenée sur cet écran après authentification.

---

### User Story 2 - Payer en ligne au moment de la demande de RDV (Priority: P2)

En tant que cliente en train de réserver un créneau, je veux pouvoir payer immédiatement en ligne via Mobile Money plutôt que de payer sur place, pour sécuriser ma réservation et éviter la manipulation d'espèces au salon.

**Why this priority**: Apporte de la valeur commerciale (réduction des no-show, cash-flow) mais dépend d'une intégration paiement plus complexe (webhook, statuts asynchrones) ; elle est livrable après l'historique de base.

**Independent Test**: Depuis l'écran de demande de RDV, choisir "Payer en ligne", être redirigée vers le lien de paiement hébergé WinPayer, simuler un paiement réussi côté agrégateur (environnement TEST), et vérifier que le statut de paiement passe à "payé" après réception du webhook.

**Acceptance Scenarios**:

1. **Given** une cliente sur l'écran de demande de RDV avec une prestation sélectionnée, **When** elle choisit "Payer en ligne" et confirme, **Then** le système crée le RDV, crée une transaction de paiement liée, et la redirige vers le lien de paiement hébergé WinPayer.
2. **Given** une transaction de paiement en attente, **When** le webhook WinPayer confirme un paiement réussi, **Then** le statut de paiement de la transaction passe à "payé" et ce statut est visible sur l'écran de confirmation et dans l'historique.
3. **Given** une transaction de paiement en attente, **When** le webhook WinPayer signale un échec, **Then** le statut passe à "échoué" et la cliente est informée qu'elle peut réessayer ou payer sur place.
4. **Given** une cliente sur l'écran de demande de RDV, **When** elle choisit "Payer sur place" (comportement actuel), **Then** le RDV est créé sans transaction de paiement, comme aujourd'hui.

---

### User Story 3 - Vérifier manuellement un paiement resté en attente (Priority: P3)

En tant qu'administrateur ou cliente, je veux pouvoir déclencher une vérification manuelle du statut d'un paiement resté "en attente" trop longtemps (webhook non reçu), pour débloquer une réservation sans attendre indéfiniment.

**Why this priority**: Cas de résilience déjà couvert par un mécanisme équivalent pour les abonnements partenaires (008) ; réutilise l'infrastructure existante, priorité plus basse car c'est un filet de sécurité, pas le chemin nominal.

**Independent Test**: Avec une transaction de paiement RDV en statut "en attente" côté base, déclencher la vérification manuelle et constater que le statut se met à jour selon la réponse réelle de l'agrégateur WinPayer.

**Acceptance Scenarios**:

1. **Given** une transaction de paiement RDV en attente depuis plus de quelques minutes, **When** une vérification manuelle est déclenchée, **Then** le système interroge WinPayer et met à jour le statut local en conséquence (payé, échoué, ou toujours en attente).

### Edge Cases

- Que se passe-t-il si le client ferme l'application après avoir été redirigé vers WinPayer mais avant la fin du paiement ? → Le RDV reste créé avec paiement "en attente" ; il redevient consultable dans l'historique avec ce statut, et une vérification manuelle reste possible.
- Que se passe-t-il si le webhook WinPayer arrive pour un RDV déjà refusé/annulé par le salon entre-temps ? → Le statut du paiement est mis à jour normalement (traçabilité financière), mais le système n'essaie pas de "reconfirmer" un RDV déjà refusé/annulé.
- Que se passe-t-il si le client tente de payer en ligne deux fois pour le même RDV ? → Le système réutilise/complète la transaction de paiement existante liée à ce RDV plutôt que d'en créer une seconde (idempotence, même principe que la feature 008).
- Que se passe-t-il pour l'historique si l'établissement ou la prestation d'un ancien RDV a depuis été supprimé(e) ? → Le RDV reste visible dans l'historique avec les informations figées au moment de la réservation (nom d'établissement et de prestation ne disparaissent pas silencieusement).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Le système DOIT permettre à un client authentifié de lister l'ensemble de ses rendez-vous (tous statuts confondus), triés du plus récent au plus ancien par date/heure de rendez-vous.
- **FR-002**: Chaque rendez-vous de la liste DOIT afficher : le nom de l'établissement, le libellé de la prestation, la date/heure, et le statut courant du rendez-vous.
- **FR-003**: Le système DOIT refuser l'accès à l'historique d'un client à toute personne non authentifiée en tant que ce client (un client ne peut voir que ses propres rendez-vous).
- **FR-004**: Le système DOIT permettre, au moment de la demande de rendez-vous, de choisir entre "paiement sur place" (comportement actuel, inchangé) et "paiement en ligne".
- **FR-005**: Si le client choisit le paiement en ligne, le système DOIT créer une transaction de paiement liée au rendez-vous et retourner un lien de paiement hébergé (WinPayer) vers lequel rediriger le client.
- **FR-006**: Le système DOIT recevoir et traiter la confirmation de paiement via un webhook, en vérifiant l'authenticité du webhook selon le même mécanisme de signature que celui déjà utilisé pour les paiements d'abonnement (008).
- **FR-007**: Le système DOIT exposer le statut de paiement courant (en attente / payé / échoué) d'un rendez-vous, consultable à la fois sur l'écran de confirmation immédiatement après la demande et dans l'historique.
- **FR-008**: Le système DOIT permettre de déclencher une vérification manuelle du statut d'une transaction de paiement restée en attente, en interrogeant directement l'agrégateur WinPayer.
- **FR-009**: Le système DOIT garantir l'idempotence : une nouvelle tentative de paiement en ligne pour un rendez-vous ayant déjà une transaction de paiement en cours ne DOIT PAS créer de transaction dupliquée.
- **FR-010**: Le système NE DOIT PAS modifier le statut du rendez-vous lui-même (DEMANDE/CONFIRME/REFUSE/ANNULE/TERMINE) suite à un événement de paiement ; le statut de paiement et le statut du rendez-vous restent des informations distinctes et indépendantes.
- **FR-011**: Le système DOIT conserver dans l'historique le nom de l'établissement et le libellé de la prestation tels qu'ils étaient au moment de la réservation, même si l'établissement ou la prestation change ou est supprimé(e) par la suite.

### Key Entities

- **RendezVous (existant, étendu)** : représente une réservation d'un client auprès d'un établissement pour une prestation à une date/heure donnée, avec un statut de cycle de vie (DEMANDE, CONFIRME, REFUSE, ANNULE, TERMINE). Peut désormais être associé à zéro ou une transaction de paiement.
- **TransactionPaiementRdv (nouveau)** : représente une tentative de paiement en ligne liée à un rendez-vous précis. Attributs clés : montant, moyen de paiement (Mobile Money), statut (en attente, payé, échoué), référence externe de l'agrégateur, horodatages de création et de dernière mise à jour.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Une cliente ayant déjà pris au moins un rendez-vous peut retrouver l'ensemble de son historique en moins de 5 secondes après ouverture de l'écran dédié.
- **SC-002**: 100% des rendez-vous listés dans l'historique affichent un statut cohérent avec l'état réel du rendez-vous côté salon (pas de désynchronisation observable).
- **SC-003**: Pour une transaction de paiement en ligne réussie, le statut "payé" est visible côté client en moins de 2 minutes après la confirmation réelle du paiement par l'opérateur Mobile Money (délai dominé par la latence du webhook agrégateur, hors contrôle direct du système).
- **SC-004**: 0% des tentatives de paiement en ligne répétées sur un même rendez-vous ne créent de transaction dupliquée facturée deux fois.

## Assumptions

- Le mécanisme d'authentification client (OTP, feature 003) et l'identifiant client transmis via en-tête `X-Client-Id` restent le mécanisme d'autorisation utilisé pour restreindre l'accès à l'historique — cohérent avec le reste de l'API existante.
- L'agrégateur de paiement reste WinPayer, déjà intégré pour les abonnements partenaires (008) ; cette feature réutilise le même gateway, le même mécanisme de vérification de signature de webhook, et le même environnement TEST tant que la PROD n'est pas activée.
- Le paiement en ligne concerne uniquement le montant de la prestation réservée (pas d'acompte partiel ni de pourboire à ce stade — ces sujets relèvent de parcours futurs comme "Kéké Protect" et les pourboires Wave).
- Aucune fonctionnalité de remboursement automatique n'est incluse dans cette feature ; un paiement en ligne pour un RDV ensuite refusé ou annulé reste un cas traité manuellement/hors-scope pour l'instant (sera couvert par une feature dédiée type "Kéké Protect", Parcours 2).
- La pagination de l'historique n'est pas requise pour le MVP : le volume de rendez-vous par client reste faible dans le contexte actuel du produit.
