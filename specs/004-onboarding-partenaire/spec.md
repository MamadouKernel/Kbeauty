# Feature Specification: Inscription et Validation KYC des Établissements Partenaires

**Feature Branch**: `004-onboarding-partenaire`

**Created**: 2026-09-18

**Status**: Draft

**Input**: User description: "Inscription et validation KYC des établissements partenaires (features US-04 et US-05 du Product Backlog). Le système doit permettre à un gérant d'établissement (salon de coiffure, spa, massage, onglerie) de créer un compte partenaire en fournissant : ses informations de base (nom de l'établissement, localisation GPS exacte, numéro de service client, horaires), une photo de sa devanture/profil, et la pièce d'identité du gérant. Ce compte partenaire est distinct d'un éventuel compte client déjà existant pour le même numéro de téléphone (un même numéro peut avoir les deux). Tant que le dossier n'a pas été validé par un administrateur, l'établissement n'est ni visible ni recherchable par les clients dans l'annuaire. Un administrateur doit pouvoir consulter la liste des dossiers en attente, examiner les pièces fournies (photo devanture, pièce d'identité), puis valider ou rejeter le dossier. En cas de validation, l'établissement devient visible dans l'annuaire (sous réserve d'avoir au moins une prestation et une catégorie, règles déjà couvertes par d'autres features). En cas de rejet, le gérant doit être informé du refus. Cette feature s'appuie sur le squelette applicatif (002-scaffold-dotnet), sur l'entité Etablissement déjà modélisée en MERISE, et sur les règles de gestion RG-ID-02, RG-ETB-01, RG-ADM-01 déjà documentées dans docs/merise/01-regles-gestion.md."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Soumission d'un dossier d'inscription partenaire (Priority: P1)

Un gérant d'établissement de beauté soumet son numéro de téléphone, les informations de base de son
établissement, une photo de sa devanture et sa pièce d'identité, afin de créer un compte partenaire
et démarrer le processus de validation.

**Why this priority**: Sans cette soumission, aucun établissement ne peut jamais apparaître dans
l'annuaire — c'est le point d'entrée obligatoire du parcours B2B (CDC §3.2).

**Independent Test**: Peut être testé seul en soumettant un dossier complet pour un numéro de
téléphone jamais utilisé côté partenaire, et en constatant la création d'un dossier en attente.

**Acceptance Scenarios**:

1. **Given** un gérant qui n'a pas encore de compte partenaire, **When** il soumet son numéro, les
   informations de son établissement, une photo de devanture et une pièce d'identité, **Then** un
   compte partenaire et un dossier établissement sont créés avec un statut "en attente de validation".
2. **Given** un dossier soumis avec une information de base manquante (ex. localisation GPS), **When**
   la soumission est traitée, **Then** elle est rejetée avant création, avec un message explicite sur
   ce qui manque.
3. **Given** un numéro de téléphone déjà associé à un compte CLIENT existant, **When** ce même numéro
   est utilisé pour soumettre un dossier partenaire, **Then** la soumission réussit et un compte
   PARTENAIRE distinct est créé (les deux comptes coexistent, RG-ID-04).

---

### User Story 2 - Consultation et décision administrateur (Priority: P1)

Un administrateur consulte la liste des dossiers partenaires en attente de validation, examine les
pièces fournies pour un dossier donné, puis le valide ou le rejette.

**Why this priority**: Sans cette étape, aucun établissement soumis ne peut jamais devenir visible —
c'est le goulot obligatoire entre la soumission (US1) et la visibilité dans l'annuaire.

**Independent Test**: Peut être testé seul avec un dossier déjà en attente : consulter la liste,
ouvrir le dossier, décider (valider ou rejeter), et constater le changement de statut.

**Acceptance Scenarios**:

1. **Given** un ou plusieurs dossiers en attente, **When** un administrateur consulte la liste,
   **Then** il voit chaque dossier avec au minimum le nom de l'établissement et sa date de soumission.
2. **Given** un dossier en attente ouvert, **When** l'administrateur consulte les détails, **Then** il
   peut visualiser la photo de devanture et la pièce d'identité fournies.
3. **Given** un dossier en attente, **When** l'administrateur le valide, **Then** le statut passe à
   "validé" et l'établissement devient éligible à l'affichage dans l'annuaire (sous réserve des autres
   règles déjà couvertes ailleurs : au moins une prestation et une catégorie).
4. **Given** un dossier en attente, **When** l'administrateur le rejette, **Then** le statut passe à
   "rejeté" et le gérant est informé du refus.

---

### User Story 3 - Invisibilité de l'établissement tant que non validé (Priority: P2)

Un client qui recherche dans l'annuaire ne doit jamais voir un établissement dont le dossier partenaire
est encore en attente ou a été rejeté.

**Why this priority**: Protège l'intégrité de l'annuaire (CDC §5), mais dépend des User Stories 1 et 2
pour qu'un dossier existe et ait un statut à vérifier — non bloquante pour livrer US1/US2 seules.

**Independent Test**: Peut être testé en comparant le résultat d'une recherche client avant et après
la validation d'un dossier partenaire donné.

**Acceptance Scenarios**:

1. **Given** un établissement dont le dossier est "en attente" ou "rejeté", **When** un client effectue
   une recherche qui correspondrait par ailleurs à cet établissement, **Then** il n'apparaît pas dans
   les résultats.
2. **Given** ce même établissement une fois son dossier "validé" (et les autres conditions de
   publication remplies), **When** le client refait la même recherche, **Then** l'établissement
   apparaît désormais.

---

### Edge Cases

- Que se passe-t-il si un gérant soumet un second dossier pour un établissement alors qu'un premier
  dossier est déjà en attente pour le même numéro ? → Le second dossier remplace ou complète le
  premier ; il ne doit jamais exister deux dossiers "en attente" simultanés pour un même gérant sans
  qu'il soit clair lequel est actif.
- Que se passe-t-il si l'administrateur tente de valider un dossier auquel il manque une pièce
  (ex. pièce d'identité absente) ? → La validation est refusée tant que les deux pièces obligatoires
  (photo devanture, pièce d'identité) ne sont pas présentes.
- Comment le système réagit-il si un administrateur rejette un dossier déjà validé ? → Un dossier déjà
  validé peut être re-basculé en rejeté par un administrateur (ex. fraude détectée après coup), ce qui
  retire immédiatement l'établissement de la visibilité dans l'annuaire.
- Que se passe-t-il si le fichier fourni pour la pièce d'identité ou la photo n'est pas un format
  d'image valide ? → La soumission est rejetée avant création du dossier, avec un message explicite.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Le système MUST permettre à un gérant de soumettre, en une seule opération, son numéro
  de téléphone, les informations de base de son établissement (nom, localisation GPS, numéro de
  service client, horaires), une photo de devanture et une pièce d'identité, ces deux derniers étant
  téléversés directement via l'application (upload de fichiers, décision confirmée). L'emplacement de
  stockage précis des fichiers (local au conteneur ou service dédié) sera tranché dans le plan
  technique de cette feature (voir Assumptions).
- **FR-002**: Le système MUST créer un compte PARTENAIRE associé au numéro de téléphone soumis,
  indépendamment de l'existence d'un compte CLIENT pour ce même numéro (RG-ID-04).
- **FR-003**: Le système MUST rejeter toute soumission incomplète (information de base manquante,
  photo ou pièce d'identité absente ou dans un format non reconnu) avant toute création de dossier,
  avec un message explicite sur ce qui manque ou est invalide.
- **FR-004**: Tout établissement nouvellement soumis MUST recevoir un statut de validation "en attente"
  (RG-ETB-01) — jamais un statut "validé" par défaut.
- **FR-005**: Le système MUST permettre à un administrateur de consulter la liste des établissements
  au statut "en attente", avec au minimum le nom de l'établissement et la date de soumission.
- **FR-006**: Le système MUST permettre à un administrateur de consulter, pour un établissement donné,
  la photo de devanture et la pièce d'identité fournies.
- **FR-007**: Le système MUST permettre à un administrateur de faire passer un établissement de "en
  attente" à "validé", uniquement si la photo de devanture et la pièce d'identité sont toutes deux
  présentes (RG-ADM-01).
- **FR-008**: Le système MUST permettre à un administrateur de faire passer un établissement à
  "rejeté", que ce soit depuis "en attente" ou depuis "validé".
- **FR-009**: Le système MUST informer le gérant de tout rejet de son dossier (nouveau ou déjà validé
  précédemment).
- **FR-010**: Un établissement dont le statut n'est pas "validé" ne MUST jamais apparaître dans les
  résultats de recherche ou de consultation accessibles aux clients (RG-ETB-01).
- **FR-011**: Le système ne MUST jamais exposer la pièce d'identité d'un gérant à un client ou à un
  autre partenaire — elle est visible uniquement par les administrateurs.

### Key Entities *(include if feature involves data)*

- **Établissement** : entité déjà modélisée en MERISE (`docs/merise/03-mcd.md`) — cette feature pilote
  son cycle de vie de validation (`statut_kyc`), déjà présent dans le MPD (`EN_ATTENTE`/`VALIDE`/`REJETE`).
- **Utilisateur (type PARTENAIRE)** : entité déjà modélisée — cette feature crée des instances de ce
  type, rattachées à l'établissement soumis en tant que gérant.
- **Pièces jointes** (photo devanture, pièce d'identité) : déjà référencées comme colonnes de
  l'établissement (`url_photo_devanture`, `url_piece_identite`) — cette feature définit le processus
  de soumission et de consultation, pas un nouveau modèle de stockage.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Un gérant peut soumettre un dossier complet (informations, photo, pièce d'identité) en
  moins de 5 minutes dans le cas nominal.
- **SC-002**: 100% des établissements au statut autre que "validé" sont absents des résultats de
  recherche client (0 fuite constatée en test).
- **SC-003**: Un administrateur peut passer d'un dossier "en attente" à une décision ("validé" ou
  "rejeté") en consultant uniquement les informations et pièces déjà fournies, sans étape externe.
- **SC-004**: 100% des rejets déclenchent une notification explicite au gérant concerné.

## Assumptions

- Le canal de notification du gérant en cas de rejet (FR-009) réutilisera le mécanisme déjà en place
  pour les comptes (ex. WhatsApp via Zavu, une fois configuré) plutôt qu'un nouveau canal dédié ; le
  choix technique précis sera tranché dans le plan de cette feature.
- La transmission des fichiers (photo, pièce d'identité) se fait par upload direct via l'application
  (décision confirmée). L'emplacement physique de stockage (volume local du conteneur pour démarrer,
  remplaçable plus tard par un service de stockage dédié sans changer le contrat d'API) sera précisé
  dans le plan technique de cette feature.
- La décision administrateur (valider/rejeter) est prise par un être humain via une interface
  d'administration ; aucune validation automatique (ex. reconnaissance faciale, OCR) n'est dans le
  périmètre de cette feature.
- Un seul rôle "administrateur" existe pour cette feature (pas de rôles différenciés type
  modérateur/superviseur) — cohérent avec le Product Backlog actuel.
