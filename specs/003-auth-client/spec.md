# Feature Specification: Inscription et Authentification Client par Téléphone

**Feature Branch**: `003-auth-client`

**Created**: 2026-09-18

**Status**: Draft

**Input**: User description: "Inscription et authentification du client (feature US-03 du Product Backlog). Le système doit permettre à une personne recherchant des soins de beauté de créer un compte en utilisant uniquement son numéro de téléphone, sans mot de passe : elle saisit son numéro, reçoit un code de vérification à usage unique sur ce numéro (canal exact encore à confirmer côté fournisseur — WhatsApp ou SMS), saisit ce code, et obtient un compte client actif. Une fois le compte créé, le client doit pouvoir se reconnecter ultérieurement avec le même mécanisme (numéro + nouveau code de vérification), sans avoir à mémoriser de mot de passe. Le code de vérification doit expirer après un court délai et ne doit pouvoir être utilisé qu'une seule fois. Un numéro de téléphone ne peut correspondre qu'à un seul compte client (règle de gestion RG-ID-01/RG-ID-04 déjà définie dans docs/merise/01-regles-gestion.md). Cette fonctionnalité s'appuie sur le squelette applicatif déjà en place (feature 002-scaffold-dotnet) et sur l'entité Utilisateur déjà modélisée en MERISE."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Première inscription par téléphone (Priority: P1)

Une personne qui recherche des soins de beauté et n'a pas encore de compte saisit son numéro de
téléphone, reçoit un code de vérification à usage unique, le saisit, et obtient un compte client
actif — sans jamais avoir à créer ou mémoriser de mot de passe.

**Why this priority**: Sans inscription, aucun client ne peut interagir avec l'annuaire (appeler,
prendre RDV). C'est le point d'entrée obligatoire de tout le parcours B2C (CDC §3.1).

**Independent Test**: Peut être testé seul en saisissant un numéro de téléphone jamais utilisé,
en récupérant le code envoyé, en le saisissant, et en constatant la création d'un compte actif.

**Acceptance Scenarios**:

1. **Given** un numéro de téléphone jamais enregistré, **When** la personne le soumet, **Then** un
   code de vérification à usage unique lui est envoyé sur ce numéro.
2. **Given** un code de vérification valide reçu, **When** la personne le saisit avant expiration,
   **Then** un compte client est créé et actif, associé à ce numéro de téléphone.
3. **Given** un code de vérification erroné, **When** la personne le saisit, **Then** l'inscription
   est refusée avec un message explicite, sans création de compte.

---

### User Story 2 - Reconnexion ultérieure (Priority: P1)

Un client déjà inscrit revient sur l'application et se reconnecte en utilisant à nouveau son numéro
de téléphone et un nouveau code de vérification, sans avoir à saisir de mot de passe.

**Why this priority**: Aussi essentielle que l'inscription initiale — un client qui ne peut pas se
reconnecter facilement abandonne l'usage de l'application. Même mécanisme technique que US1.

**Independent Test**: Peut être testé seul en soumettant un numéro déjà associé à un compte client
existant, en recevant un nouveau code, en le saisissant, et en constatant l'accès au compte existant
(pas la création d'un second compte).

**Acceptance Scenarios**:

1. **Given** un numéro de téléphone déjà associé à un compte client, **When** la personne le soumet
   à nouveau, **Then** un nouveau code de vérification lui est envoyé, distinct du précédent.
2. **Given** ce nouveau code saisi correctement, **When** la personne valide, **Then** elle accède à
   son compte client existant (aucun doublon de compte créé).

---

### User Story 3 - Expiration et usage unique du code (Priority: P2)

Un utilisateur qui tente de réutiliser un ancien code, ou d'utiliser un code après le délai autorisé,
se voit refuser l'opération et doit en demander un nouveau.

**Why this priority**: Protège l'accès aux comptes contre la réutilisation d'un code intercepté ou
oublié, mais n'empêche pas les Users Stories 1/2 de fonctionner pour le cas nominal.

**Independent Test**: Peut être testé seul en réutilisant un code déjà consommé, ou en attendant
l'expiration d'un code non utilisé, puis en tentant de le valider.

**Acceptance Scenarios**:

1. **Given** un code de vérification déjà utilisé avec succès, **When** quelqu'un tente de le
   réutiliser, **Then** la tentative est refusée.
2. **Given** un code de vérification non utilisé après le délai d'expiration, **When** quelqu'un
   tente de le valider, **Then** la tentative est refusée et un nouveau code doit être demandé.

---

### Edge Cases

- Que se passe-t-il si la même personne demande plusieurs codes de vérification à la suite pour le
  même numéro avant d'en valider un ? → Seul le dernier code émis doit rester valide ; les précédents
  sont automatiquement invalidés.
- Comment le système réagit-il si l'envoi du code de vérification échoue côté fournisseur (WhatsApp
  ou SMS indisponible) ? → La personne doit recevoir un message d'échec explicite l'invitant à
  réessayer, pas un succès trompeur ni un blocage silencieux.
- Que se passe-t-il si quelqu'un tente de soumettre un numéro de téléphone dans un format invalide ?
  → La demande est rejetée avant tout envoi de code, avec un message explicite sur le format attendu.
- Que se passe-t-il si un numéro déjà utilisé pour un compte PARTENAIRE ou ADMIN est soumis pour une
  inscription CLIENT ? → Autorisé : un même numéro peut correspondre à un compte CLIENT et à un compte
  PARTENAIRE distincts (RG-ID-04), mais jamais à deux comptes CLIENT.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Le système MUST permettre à une personne de démarrer une inscription ou une connexion
  en soumettant uniquement un numéro de téléphone (aucun mot de passe requis).
- **FR-002**: Le système MUST envoyer, suite à cette soumission, un code de vérification à usage
  unique au numéro de téléphone fourni, via WhatsApp en utilisant le fournisseur Zavu. L'envoi réel
  reste dépendant de la configuration complète du projet Zavu dédié à Keke Beauty (en cours) ; le
  système MUST néanmoins être conçu de sorte que l'envoi soit isolé derrière une interface dédiée,
  activable dès que cette configuration est finalisée (voir Assumptions).
- **FR-003**: Le système MUST valider le code de vérification saisi et, si correct et non expiré,
  créer un compte client actif (si le numéro est nouveau) ou donner accès au compte existant (si le
  numéro est déjà associé à un compte CLIENT).
- **FR-004**: Le système MUST refuser explicitement toute validation avec un code incorrect, sans
  créer ni modifier de compte.
- **FR-005**: Un code de vérification MUST expirer après un délai court (5 minutes par défaut, cf.
  `docs/merise/02-dictionnaire-donnees.md`) et MUST devenir invalide immédiatement après un seul
  usage réussi.
- **FR-006**: Lorsqu'un nouveau code de vérification est émis pour un même numéro, tout code
  précédemment émis et non utilisé pour ce numéro MUST être invalidé.
- **FR-007**: Le système MUST garantir qu'un numéro de téléphone ne correspond jamais à plus d'un
  compte CLIENT (RG-ID-01, RG-ID-04), tout en autorisant qu'il corresponde par ailleurs à un compte
  PARTENAIRE ou ADMIN distinct.
- **FR-008**: Le système MUST rejeter, avant tout envoi de code, un numéro de téléphone dans un
  format invalide, avec un message explicite.
- **FR-009**: Le système MUST signaler explicitement à l'utilisateur tout échec d'envoi du code de
  vérification (indisponibilité du fournisseur), sans jamais indiquer un succès trompeur.
- **FR-010**: Le système ne MUST jamais exposer ou journaliser en clair le code de vérification dans
  un contexte accessible autrement que par son envoi au numéro de téléphone concerné (cf. Principe V
  de la constitution — sécurité des données sensibles).

### Key Entities *(include if feature involves data)*

- **Utilisateur (type CLIENT)** : entité déjà modélisée en MERISE (`docs/merise/03-mcd.md`) — cette
  feature crée et authentifie des instances de type CLIENT, sans modifier la structure de l'entité.
- **Code de vérification (OTP)** : donnée temporaire associée à un numéro de téléphone, avec un statut
  (valide/consommé/expiré), déjà référencée dans le dictionnaire de données (`otp_code`, §02).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Une personne peut passer de la saisie de son numéro de téléphone à un compte client
  actif en moins de 2 minutes dans le cas nominal (code reçu et saisi sans erreur).
- **SC-002**: 100% des tentatives de réutilisation d'un code déjà consommé ou expiré sont refusées.
- **SC-003**: 0 numéro de téléphone ne se retrouve associé à plus d'un compte client, vérifié sur
  l'ensemble des inscriptions réalisées.
- **SC-004**: 100% des échecs d'envoi du code de vérification sont signalés explicitement à
  l'utilisateur (aucun cas de succès trompeur observé en test).

## Assumptions

- Le canal d'envoi du code est WhatsApp via Zavu (décision confirmée). Le projet Zavu dédié à Keke
  Beauty est en cours de configuration au moment de la rédaction de cette spec : l'envoi réel via
  Zavu est une **dépendance bloquante** pour la validation de bout en bout des User Stories 1/2/3 en
  conditions réelles, mais ne bloque pas la conception ni l'implémentation du reste du flux (génération
  et validation du code, création/accès au compte), qui peuvent être développés et testés dès
  maintenant derrière une interface d'envoi dédiée.
- La durée d'expiration par défaut du code est de 5 minutes et sa longueur de 6 chiffres, conformément
  aux valeurs déjà posées dans `docs/merise/02-dictionnaire-donnees.md`.
- Aucune limite de fréquence (rate limiting) sur les demandes de code n'est spécifiée dans cette
  feature ; elle pourra être ajoutée ultérieurement si un abus est constaté (hors périmètre initial).
- Le "compte actif" créé par cette feature ne porte que l'identité minimale (téléphone, nom optionnel,
  type de compte) ; les informations de profil étendues ne sont pas dans le périmètre de cette feature.
