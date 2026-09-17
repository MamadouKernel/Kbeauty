# Feature Specification: Infrastructure Base de Données Reproductible

**Feature Branch**: `001-infra-postgres`

**Created**: 2026-09-17

**Status**: Draft

**Input**: User description: "Infrastructure base de données PostgreSQL pour Keke Beauty (feature US-01/US-02 du Product Backlog). Le système doit fournir un environnement de base de données reproductible pour toute l'équipe : un membre de l'équipe peut démarrer une base de données locale identique en une seule commande, sans configuration manuelle. Le schéma de données doit refléter fidèlement le modèle MERISE déjà validé (docs/merise/03-mcd.md, 04-mld.md, 05-mpd.sql) : utilisateurs (clients/partenaires/admin), établissements avec leur localisation géographique (pays/région/ville/commune) et catégories, médias, prestations et tarifs, rendez-vous, abonnements et transactions de paiement. Toute évolution future du schéma doit être traçable et reproductible depuis un environnement vide (pas de modification manuelle non versionnée). Les acteurs concernés : l'équipe de développement (qui a besoin de l'environnement) et, indirectement, tous les futurs modules fonctionnels (identité, annuaire, RDV, paiement) qui dépendront de ce schéma."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Démarrage d'un environnement de base de données local (Priority: P1)

Un membre de l'équipe de développement, qui vient de récupérer le projet, veut disposer d'une base
de données locale contenant déjà toutes les tables métier (utilisateurs, établissements, prestations,
RDV, abonnements, transactions) sans devoir configurer manuellement un serveur ou écrire le schéma
à la main.

**Why this priority**: Sans cet environnement, aucun autre module fonctionnel (identité, annuaire,
RDV, paiement) ne peut être développé ni testé. C'est le prérequis bloquant de tout le projet.

**Independent Test**: Peut être testé seul en partant d'une machine sans base de données existante,
en exécutant une seule commande de démarrage, puis en vérifiant que toutes les tables attendues
existent et sont vides.

**Acceptance Scenarios**:

1. **Given** une machine avec Docker Desktop installé et le projet cloné, **When** le développeur
   exécute la commande de démarrage de l'environnement, **Then** une base de données locale est
   disponible avec l'ensemble des tables du modèle MERISE déjà créées.
2. **Given** l'environnement déjà démarré une première fois, **When** le développeur relance la
   commande de démarrage, **Then** les données existantes ne sont pas perdues et l'environnement
   redémarre à l'identique.

---

### User Story 2 - Évolution traçable du schéma (Priority: P2)

Un membre de l'équipe technique veut faire évoluer le schéma de données (ajout d'une colonne, d'une
table, d'une contrainte) sans jamais modifier une base existante à la main, afin que tout autre
membre de l'équipe ou tout environnement (test, production) puisse reproduire exactement le même état.

**Why this priority**: Essentiel à la fiabilité à moyen terme, mais le projet peut démarrer avec un
schéma initial figé (P1) avant que ce besoin d'évolution ne se présente.

**Independent Test**: Peut être testé en partant d'un environnement vide, en appliquant l'ensemble
des évolutions de schéma dans l'ordre, et en vérifiant que le résultat final est identique à celui
obtenu par n'importe quel autre membre de l'équipe appliquant les mêmes évolutions.

**Acceptance Scenarios**:

1. **Given** un environnement de base de données vide, **When** l'ensemble des évolutions de schéma
   versionnées est appliqué dans l'ordre, **Then** le schéma final correspond exactement au modèle
   MERISE validé (MCD/MLD/MPD).
2. **Given** une évolution de schéma proposée, **When** elle est ajoutée au projet, **Then** elle est
   traçable (fichier versionné, horodaté, non modifiable rétroactivement sans nouvelle évolution).

---

### Edge Cases

- Que se passe-t-il si le port réseau par défaut de la base de données est déjà utilisé par un autre
  service sur la machine du développeur ? → Le point de configuration du port doit être ajustable
  sans modifier le schéma ni le code applicatif.
- Comment le système réagit-il si une évolution de schéma référence une table ou une colonne qui
  n'existe pas encore (ordre d'application incorrect) ? → L'application doit échouer explicitement
  plutôt que de créer un état partiel silencieux.
- Que se passe-t-il si deux évolutions de schéma tentent de créer le même élément (conflit) ? →
  Le système doit détecter le conflit et empêcher un état incohérent.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Le système MUST permettre de démarrer un environnement de base de données local complet
  au moyen d'une seule commande, sans étape de configuration manuelle supplémentaire.
- **FR-002**: Le système MUST créer automatiquement, au premier démarrage, l'ensemble des tables
  métier définies dans le modèle MERISE validé (utilisateurs, localisation géographique, catégories,
  établissements, médias, prestations, rendez-vous, abonnements, transactions).
- **FR-003**: Le système MUST conserver les données existantes entre deux redémarrages de
  l'environnement (persistance au-delà du cycle de vie du processus).
- **FR-004**: Toute évolution du schéma de données MUST être définie dans un fichier versionné et
  reproductible, jamais appliquée par une modification manuelle directe sur une base existante.
- **FR-005**: Le système MUST permettre de reconstruire l'intégralité du schéma à jour depuis un
  environnement complètement vide, en appliquant uniquement les fichiers d'évolution versionnés.
- **FR-006**: Le système MUST permettre d'ajuster le point de connexion réseau (port) de
  l'environnement local sans modifier le schéma de données ni le code applicatif.
- **FR-007**: Le système MUST refuser toute évolution de schéma incohérente : toute évolution
  appliquée dans le désordre ou en conflit avec l'existant MUST provoquer un échec explicite.
- **FR-008**: Le schéma créé MUST respecter les règles de gestion déjà validées (docs/merise/01-regles-gestion.md),
  notamment l'unicité du numéro de téléphone par type de compte et le rattachement obligatoire d'un
  établissement à une zone géographique et à au moins une catégorie.

### Key Entities *(include if feature involves data)*

- **Utilisateur** : personne physique identifiée par téléphone, de type client, partenaire ou
  administrateur.
- **Établissement** : commerce de beauté rattaché à un utilisateur-gérant, à une zone géographique
  et à une ou plusieurs catégories ; possède des médias et des prestations.
- **Zone géographique** (Pays/Région/Ville/Commune) : référentiel hiérarchique de localisation.
- **Catégorie** : type d'établissement (salon de coiffure, spa, massage, onglerie, etc.).
- **Prestation** : service proposé par un établissement, avec tarif et durée.
- **Rendez-vous** : demande de créneau d'un client auprès d'un établissement pour une prestation donnée.
- **Abonnement** : souscription d'un établissement donnant accès aux fonctionnalités avancées.
- **Transaction** : paiement associé à un abonnement.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Un nouveau membre de l'équipe peut disposer d'un environnement de base de données
  local complet et opérationnel en moins de 5 minutes après avoir récupéré le projet.
- **SC-002**: 100% des tables métier définies par le modèle MERISE validé sont présentes et
  correctement reliées (aucune contrainte manquante) après le premier démarrage de l'environnement.
- **SC-003**: Un environnement reconstruit intégralement depuis zéro produit un schéma identique
  à celui d'un environnement existant, sans intervention manuelle.
- **SC-004**: Aucune modification de schéma n'est appliquée en dehors d'un fichier d'évolution
  versionné (0 modification manuelle non traçable constatée en revue).

## Assumptions

- L'équipe de développement dispose de Docker Desktop installé sur sa machine (déjà confirmé par
  l'utilisateur du projet).
- L'environnement local sert de référence de développement ; l'environnement de production suivra
  le même schéma versionné mais via un pipeline de déploiement distinct (hors périmètre de cette feature).
- Le volume de données en développement reste modeste (jeux de données de test), la performance à
  grande échelle n'est pas un objectif de cette feature.
- Un seul schéma de base de données sert l'ensemble des modules fonctionnels futurs (pas de
  séparation en plusieurs bases à ce stade).
