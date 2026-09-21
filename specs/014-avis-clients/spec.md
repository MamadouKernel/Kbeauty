# Feature Specification: Avis clients après rendez-vous

**Feature Branch**: `014-avis-clients`

**Created**: 2026-09-19

**Status**: Draft

**Input**: User description: "Completer le parcours client a 100% : permettre a un client de laisser un avis (note + commentaire) apres un rendez-vous termine, et afficher les avis reels (note moyenne, liste) sur la fiche salon, en remplacement du placeholder 'bientot disponible'."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Laisser un avis après un rendez-vous terminé (Priority: P1)

En tant que cliente dont le rendez-vous est marqué "Terminé", je veux noter le salon (1 à 5 étoiles) et laisser un commentaire, pour partager mon expérience.

**Why this priority**: C'est le cœur de la feature — sans ça, rien à afficher sur la fiche salon.

**Independent Test**: Depuis "Mes rendez-vous", sur un RDV au statut TERMINE, soumettre une note et un commentaire ; vérifier que l'avis est enregistré et lié à ce RDV.

**Acceptance Scenarios**:

1. **Given** un RDV au statut TERMINE sans avis existant, **When** la cliente soumet une note (1-5) et un commentaire optionnel, **Then** l'avis est créé et associé à ce RDV.
2. **Given** un RDV qui n'est pas au statut TERMINE, **When** la cliente tente de laisser un avis, **Then** le système refuse (le RDV doit être terminé).
3. **Given** un RDV ayant déjà un avis, **When** la cliente retente d'en laisser un second, **Then** le système refuse (un seul avis par RDV).
4. **Given** un RDV appartenant à une autre cliente, **When** une cliente tente d'y laisser un avis, **Then** le système refuse (404, pas de fuite d'information).

---

### User Story 2 - Consulter les avis d'un salon (Priority: P2)

En tant que visiteuse de la fiche salon, je veux voir la note moyenne et les avis récents, pour évaluer la qualité du salon avant de réserver.

**Why this priority**: Dépend d'US1 pour avoir des données réelles à afficher ; sans avis, l'onglet affiche un état vide honnête plutôt qu'un placeholder statique.

**Independent Test**: Ouvrir la fiche d'un salon ayant reçu au moins un avis et vérifier que la note moyenne et le commentaire apparaissent ; ouvrir la fiche d'un salon sans avis et vérifier l'état vide.

**Acceptance Scenarios**:

1. **Given** un salon ayant reçu 2 avis (notes 4 et 5), **When** un visiteur ouvre l'onglet Avis de sa fiche, **Then** la note moyenne (4.5) et les 2 avis s'affichent, triés du plus récent au plus ancien.
2. **Given** un salon sans aucun avis, **When** un visiteur ouvre l'onglet Avis, **Then** un message honnête indique l'absence d'avis (pas de note inventée).

### Edge Cases

- Un commentaire est optionnel ; la note (1-5) est obligatoire.
- Les avis ne sont jamais modifiables ou supprimables par le client dans ce périmètre (pas de UPDATE/DELETE exposé) — cohérent avec l'absence de modération d'avis prévue à ce stade.
- Un salon suspendu ou dont le KYC n'est plus VALIDE continue d'exposer ses avis existants via l'endpoint public (déjà cohérent avec le comportement actuel de la fiche : la fiche elle-même devient inaccessible via `GetValidatedCoreAsync`, donc l'onglet avis n'est de toute façon plus atteignable dans ce cas).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Le système DOIT permettre à un client authentifié de créer un avis (note 1-5, commentaire optionnel) pour un rendez-vous qui lui appartient et dont le statut est TERMINE.
- **FR-002**: Le système DOIT refuser la création d'un avis pour un rendez-vous qui n'est pas au statut TERMINE.
- **FR-003**: Le système DOIT garantir un seul avis par rendez-vous (contrainte d'unicité).
- **FR-004**: Le système DOIT refuser la création d'un avis pour un rendez-vous n'appartenant pas au client authentifié, sans distinguer "inexistant" de "appartient à un autre" (pas de fuite d'information).
- **FR-005**: Le système DOIT exposer publiquement, pour un établissement donné, la liste des avis (note, commentaire, date) triée du plus récent au plus ancien, et la note moyenne.
- **FR-006**: Le système NE DOIT JAMAIS afficher de note ou d'avis fictif quand un établissement n'a reçu aucun avis réel.
- **FR-007**: L'historique des rendez-vous du client (feature 013) DOIT indiquer, pour chaque RDV terminé, si un avis a déjà été laissé (pour ne pas proposer deux fois le même formulaire).

### Key Entities

- **Avis (nouveau)** : note (1-5), commentaire optionnel, date de création, lié à exactement un rendez-vous (via lequel on retrouve le client et l'établissement — pas de duplication de ces références).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Un client peut soumettre un avis sur un RDV terminé en moins de 30 secondes depuis "Mes rendez-vous".
- **SC-002**: La note moyenne affichée sur une fiche salon correspond exactement à la moyenne arithmétique des avis réels en base pour cet établissement.
- **SC-003**: 0% des fiches salon sans avis n'affichent une note ou un avis inventé.

## Assumptions

- Pas de modération des avis dans ce périmètre (pas de signalement, pas de suppression admin) — pourra être ajouté ultérieurement sans changer le modèle de données de base.
- Pas de réponse du salon aux avis dans ce périmètre.
- Le pourboire direct au praticien (Parcours 3, "Avis Certifié & Pourboire Wave") reste hors périmètre : cette feature couvre uniquement l'avis, pas le pourboire, qui nécessite un flux de paiement distinct vers un praticien (entité non modélisée).
