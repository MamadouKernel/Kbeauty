<!--
Sync Impact Report
Version change: 1.0.0 → 1.1.0
Modified principles: aucun principe redéfini
Added sections: Contraintes Techniques & Données — stack applicatif Full .NET (C#) et nom de base précisés
Removed sections: none
Follow-up TODOs: TODO(RATIFICATION_DATE) — confirmer la date officielle de lancement du projet avec le porteur du projet.
-->

# Keke Beauty Constitution

## Core Principles

### I. Spec-Driven Development (Spec Kit)
Toute fonctionnalité MUST suivre le flux Spec Kit avant tout code : `/speckit-constitution` →
`/speckit-specify` → `/speckit-plan` → `/speckit-tasks` → `/speckit-implement`. Aucune implémentation
ne démarre sans une spécification (`spec.md`) et un plan (`plan.md`) validés. Les artefacts générés
(spec, plan, tasks) sont versionnés dans `specs/<feature>/` et servent de source de vérité — le code
ne doit jamais diverger silencieusement de la spécification sans mise à jour du document.

### II. Conception des Données par MERISE (NON-NEGOTIABLE)
Chaque domaine métier (utilisateurs, établissements, prestations, RDV, abonnements/paiements) MUST
être modélisé selon la méthode MERISE complète avant toute création de schéma de base de données :
- **RG** (Règles de Gestion) : règles métier explicites, numérotées, traçables vers les besoins du CDC.
- **DD** (Dictionnaire des Données) : chaque donnée nommée, typée, avec règle de validation et origine.
- **MCD** (Modèle Conceptuel des Données) : entités, associations, cardinalités, sans considération technique.
- **MLD** (Modèle Logique des Données) : traduction relationnelle du MCD (tables, clés primaires/étrangères).
- **MPD** (Modèle Physique des Données) : DDL PostgreSQL réel (types, contraintes, index).
- **MCT** (Modèle Conceptuel des Traitements) : processus métier, événements, synchronisations.
- **MOT** (Modèle Organisationnel des Traitements) : qui fait quoi, quand, sur quel poste (client, partenaire, admin).
Rationale : MERISE garantit une traçabilité règle métier → donnée → traitement → schéma, essentielle
pour un système multi-acteurs (B2C/B2B/Admin) avec des règles de gestion sensibles (KYC, paiement, RDV).

### III. PostgreSQL Conteneurisé comme Source de Vérité
La base de données MUST être PostgreSQL, exécutée via Docker Desktop (docker-compose) en développement,
avec un chemin de déploiement conteneurisé équivalent en production. Toute migration de schéma MUST être
versionnée (fichiers de migration SQL ou outil de migration), reproductible depuis un environnement vide
via `docker compose up`. Aucun accès direct à la base de production sans passer par les migrations versionnées.

### IV. Gouvernance Scrum
Le développement MUST être organisé en Sprints Scrum : Product Backlog priorisé, Sprint Backlog par
itération, Daily Scrum, Sprint Review, Sprint Retrospective. Chaque User Story du Product Backlog MUST
être reliée à une spécification Spec Kit (`specs/<feature>/spec.md`) avant d'entrer dans un Sprint Backlog.
La Definition of Done d'une Story inclut : spec validée, modèle MERISE à jour si données impactées,
tests passants, revue de code effectuée.

### V. Sécurité et Conformité des Données Sensibles
Toute fonctionnalité traitant des données sensibles (OTP, pièces d'identité KYC, paiement) MUST respecter :
validation stricte aux frontières (entrée utilisateur, upload, webhook de paiement), absence de secrets
en dur dans le code, chiffrement/protection des pièces d'identité stockées, jamais de log de données
personnelles ou de moyens de paiement en clair. Rationale : Keke Beauty gère des identités de gérants
(KYC) et des transactions financières (Mobile Money, cartes) — un incident de sécurité y est critique.

## Contraintes Techniques & Données

- Stack applicatif : Full .NET (C#) — backend API en ASP.NET Core, accès aux données via une couche
  dédiée (ex. Dapper ou Entity Framework Core, à confirmer dans le plan de la première feature
  applicative) ; toute couche cliente (web/mobile) future MUST être documentée dans son propre plan
  Spec Kit avant implémentation.
- Base de données : PostgreSQL 16+, orchestré localement via Docker Desktop / docker-compose, nom de
  base `kekebeautyDb`.
- Modélisation des données : dossier `docs/merise/` contenant RG, DD, MCD, MLD, MCT, MOT, MPD par domaine.
- Intégrations externes attendues (cf. CDC) : cartographie (Google Maps/Mapbox), deep-links Yango,
  SMS/OTP et notifications (Twilio/Infobip/Firebase), agrégateur de paiement local (CinetPay/PaySika/TouchPay).
- Toute nouvelle intégration externe MUST être documentée dans le plan Spec Kit correspondant (`plan.md`)
  avant implémentation.

## Processus Scrum

- Artefacts : `docs/scrum/product-backlog.md`, `docs/scrum/sprint-<n>.md` par sprint.
- Cadence : Sprint de 2 semaines par défaut (ajustable, à documenter si modifié).
- Chaque Sprint Backlog référence les User Stories et leurs specs Spec Kit associées.
- Sprint Review MUST valider la Definition of Done définie au Principe IV avant clôture du sprint.

## Governance

La constitution prévaut sur toute pratique de développement contradictoire. Toute modification de
principe MUST être proposée via une mise à jour de ce fichier, avec justification et impact sur les
specs/plans existants documenté dans le Sync Impact Report. Politique de versionnage sémantique :
MAJOR pour suppression/redéfinition incompatible d'un principe, MINOR pour ajout de principe/section,
PATCH pour clarification. Toute revue de code ou de spec MUST vérifier la conformité à cette constitution ;
toute complexité additionnelle (nouvelle dépendance, nouvel entité hors MERISE, etc.) MUST être justifiée
dans le plan Spec Kit correspondant.

**Version**: 1.1.0 | **Ratified**: TODO(RATIFICATION_DATE): date de lancement officiel à confirmer | **Last Amended**: 2026-09-18
