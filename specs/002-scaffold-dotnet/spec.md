# Feature Specification: Squelette Applicatif Backend

**Feature Branch**: `002-scaffold-dotnet`

**Created**: 2026-09-18

**Status**: Draft

**Input**: User description: "Squelette applicatif backend pour Keke Beauty (feature préalable au développement fonctionnel, stack Full .NET/C# décidée par le porteur du projet). Le système doit fournir une base d'API backend exécutable et connectée à la base de données PostgreSQL (kekebeautyDb) déjà en place, structurée de façon à ce que les futurs modules fonctionnels (identité/authentification, annuaire, prise de rendez-vous, abonnement/paiement, back-office) puissent s'y greffer sans réorganisation majeure. L'équipe de développement doit pouvoir démarrer l'API localement en une seule commande et vérifier qu'elle répond (endpoint de santé) et qu'elle peut lire/écrire dans la base de données existante. La structure du projet doit respecter une séparation claire entre la couche d'accès aux données, la logique métier et l'exposition HTTP, conforme aux entités déjà modélisées en MERISE (utilisateurs, établissements, prestations, rendez-vous, abonnements, transactions). Aucune fonctionnalité métier n'est implémentée dans cette feature : uniquement le squelette technique, la configuration de connexion à la base, et un endpoint de vérification de santé de l'application."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Démarrage local de l'API backend (Priority: P1)

Un membre de l'équipe de développement veut démarrer l'API backend localement en une seule commande
et vérifier immédiatement qu'elle fonctionne, sans configuration manuelle additionnelle au-delà de ce
qui est déjà en place (base de données Docker existante).

**Why this priority**: Aucun module fonctionnel (identité, annuaire, RDV, paiement) ne peut être
développé sans une base applicative démarrable et vérifiable. Bloquant pour tout le reste du projet.

**Independent Test**: Sur une machine avec la base de données déjà démarrée (feature 001), exécuter
la commande de démarrage de l'API, puis appeler le point de vérification de santé et constater une
réponse positive.

**Acceptance Scenarios**:

1. **Given** la base de données Keke Beauty démarrée et accessible, **When** le développeur exécute
   la commande de démarrage de l'API, **Then** l'API démarre sans erreur et écoute sur un port local.
2. **Given** l'API démarrée, **When** le développeur interroge le point de vérification de santé,
   **Then** il reçoit une réponse indiquant que l'application est opérationnelle.

---

### User Story 2 - Vérification de la connexion à la base de données (Priority: P1)

Un membre de l'équipe veut s'assurer, dès le démarrage de l'API, que celle-ci peut effectivement lire
et écrire dans la base de données Keke Beauty existante, et pas seulement démarrer sans erreur.

**Why this priority**: Une API qui démarre sans pouvoir réellement accéder à la base ne constitue pas
un socle fiable pour les modules fonctionnels suivants — cette vérification est aussi critique que le
démarrage lui-même.

**Independent Test**: Peut être testé en interrogeant un point de vérification dédié qui effectue une
opération réelle de lecture (et si possible d'écriture réversible) sur la base de données, indépendamment
du reste du système.

**Acceptance Scenarios**:

1. **Given** l'API démarrée et connectée à la base de données, **When** le développeur interroge le
   point de vérification de connexion à la base, **Then** il reçoit une confirmation que la lecture
   et l'écriture fonctionnent.
2. **Given** une base de données indisponible ou une configuration de connexion incorrecte, **When**
   le développeur interroge ce même point de vérification, **Then** il reçoit une réponse d'échec
   explicite (pas une erreur technique opaque ni un succès trompeur).

---

### User Story 3 - Structure prête à accueillir les futurs modules (Priority: P2)

Un membre de l'équipe technique veut que la structure du projet distingue clairement l'accès aux
données, la logique métier et l'exposition HTTP, afin que l'ajout d'un futur module fonctionnel
(par exemple l'authentification client) n'exige pas de réorganiser le projet existant.

**Why this priority**: Important pour la vélocité future de l'équipe, mais le projet peut déjà être
démarré et vérifié (US1/US2) avant que cette organisation ne soit pleinement exploitée par un premier
module métier.

**Independent Test**: Peut être vérifié par une revue de structure : chaque nouvelle capacité métier
future doit pouvoir être ajoutée en créant de nouveaux éléments dans des emplacements dédiés, sans
modifier la façon dont les couches communiquent entre elles.

**Acceptance Scenarios**:

1. **Given** la structure du projet en place, **When** un membre de l'équipe consulte l'organisation
   des dossiers, **Then** il identifie sans ambiguïté où se trouve l'accès aux données, où se trouve
   la logique métier, et où se trouve l'exposition HTTP.
2. **Given** cette structure, **When** un développeur ajoute mentalement une entité déjà modélisée en
   MERISE (ex. Établissement) comme premier module futur, **Then** il peut décrire sans hésitation où
   chaque partie de cette future fonctionnalité prendrait place.

---

### Edge Cases

- Que se passe-t-il si l'API démarre alors que la base de données n'est pas encore prête (conteneur en
  cours de démarrage) ? → L'API doit soit attendre/retenter la connexion, soit démarrer et signaler
  clairement l'indisponibilité via le point de vérification, sans planter silencieusement.
- Que se passe-t-il si les informations de connexion à la base sont absentes ou invalides au démarrage ?
  → L'API doit échouer de façon explicite et lisible, pas avec une erreur technique non contextualisée.
- Comment le système se comporte-t-il si le point de vérification de santé est appelé alors qu'un
  module futur (non encore développé) est en cours d'ajout ? → Hors périmètre de cette feature : le
  point de vérification ne couvre que le socle technique, pas les futurs modules.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Le système MUST permettre de démarrer l'API backend localement au moyen d'une seule
  commande, sans étape de configuration manuelle au-delà de la configuration déjà fournie par la
  feature d'infrastructure de base de données.
- **FR-002**: Le système MUST exposer un point de vérification de santé de l'application, indiquant
  si l'application est opérationnelle.
- **FR-003**: Le système MUST exposer un point de vérification distinct confirmant que la connexion à
  la base de données Keke Beauty existante permet une lecture et une écriture réelles.
- **FR-004**: Le système MUST signaler explicitement, via le point de vérification de connexion à la
  base, toute impossibilité de lire ou d'écrire dans la base de données (pas d'échec silencieux).
- **FR-005**: La structure du projet MUST séparer clairement trois responsabilités : l'accès aux
  données, la logique métier, et l'exposition des points d'entrée (HTTP).
- **FR-006**: La structure du projet MUST permettre l'ajout d'une nouvelle capacité métier (basée sur
  une entité déjà modélisée en MERISE) sans modifier l'organisation des couches existantes.
- **FR-007**: Le système ne MUST implémenter aucune règle métier fonctionnelle (identité, annuaire,
  RDV, paiement) dans le périmètre de cette feature — uniquement le socle technique et les points de
  vérification.
- **FR-008**: Le système MUST lire sa configuration de connexion à la base de données depuis un
  emplacement externe au code source (pas de valeurs codées en dur), cohérent avec la configuration
  déjà en place pour la base de données.

### Key Entities *(include if feature involves data)*

Cette feature ne crée aucune nouvelle entité métier. Elle met en place l'accès technique aux entités
déjà modélisées en MERISE (Utilisateur, Établissement, Catégorie, zones géographiques, Média,
Prestation, Rendez-vous, Abonnement, Transaction) sans les exposer fonctionnellement.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Un membre de l'équipe peut démarrer l'API backend localement et confirmer qu'elle
  répond en moins de 2 minutes après avoir récupéré le projet (base de données déjà démarrée).
- **SC-002**: 100% des appels au point de vérification de connexion à la base de données reflètent
  fidèlement l'état réel de cette connexion (aucun faux positif ni faux négatif observé en test).
- **SC-003**: L'ajout d'une future capacité métier basée sur une entité déjà modélisée peut être
  planifié sans qu'aucune réorganisation des couches existantes (données/métier/HTTP) ne soit nécessaire.

## Assumptions

- La base de données Keke Beauty (feature 001-infra-postgres) est déjà démarrée et accessible avant
  le démarrage de l'API.
- Aucune authentification ni autorisation n'est requise pour appeler les points de vérification
  introduits par cette feature (ils ne exposent aucune donnée métier sensible).
- L'équipe de développement dispose d'un environnement capable d'exécuter le stack applicatif choisi
  par le porteur du projet (Full .NET/C#).
- Le port d'écoute local de l'API est configurable, à l'image du port déjà rendu configurable pour la
  base de données, afin d'éviter tout conflit avec d'autres services locaux.
