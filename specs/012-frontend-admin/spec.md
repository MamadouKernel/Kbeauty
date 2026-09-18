# Feature Specification: Frontend admin — KYC, modération et abonnements

**Feature Branch**: `012-frontend-admin`
**Created**: 2026-09-18
**Status**: Draft
**Input**: User description: "Continuer la construction du frontend. Troisième parcours : l'administrateur (validation/rejet des dossiers KYC, modération clients/établissements, suivi et tarification des abonnements), correspondant aux User Stories déjà implémentées côté API (US-05 validation KYC, US-19 modération, US-17/US-18 abonnements). Toute l'API admin nécessaire existe déjà — aucun nouvel endpoint backend requis."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Connexion admin (Priority: P1)
Un administrateur accède à son espace en saisissant la clé d'API admin (dette technique déjà
documentée côté API — pas de vraie authentification admin encore construite).

**Independent Test**: Saisir la clé, accéder au tableau de bord ; saisir une clé invalide, être refusé.

**Acceptance Scenarios**:
1. **Given** la page de connexion admin, **When** l'administrateur saisit la clé API correcte, **Then** il accède au tableau de bord.
2. **Given** une clé incorrecte, **When** elle est soumise, **Then** un message d'erreur explicite s'affiche, sans accès au tableau de bord.

### User Story 2 - Validation et rejet des dossiers KYC (Priority: P1)
Un administrateur consulte les dossiers en attente et les valide ou les rejette.

**Independent Test**: Lister les dossiers `EN_ATTENTE`, ouvrir un dossier, le valider.

**Acceptance Scenarios**:
1. **Given** le tableau de bord admin, **When** il filtre par statut, **Then** seuls les dossiers de ce statut apparaissent.
2. **Given** un dossier `EN_ATTENTE` avec ses documents, **When** l'administrateur le valide, **Then** son statut passe à `VALIDE`.
3. **Given** un dossier incomplet, **When** la validation est tentée, **Then** un message explicite indique les documents manquants (reflet du `409` API).
4. **Given** un dossier, **When** l'administrateur le rejette, **Then** son statut passe à `REJETE`.

### User Story 3 - Modération clients et établissements (Priority: P1)
Un administrateur suspend ou réactive un compte client/partenaire ou un établissement, par
identifiant.

**Independent Test**: Suspendre un établissement (id connu depuis la liste KYC), constater son
état ; le réactiver.

**Acceptance Scenarios**:
1. **Given** un identifiant d'établissement (accessible depuis la liste des dossiers KYC), **When** l'administrateur le suspend, **Then** une confirmation s'affiche.
2. **Given** un identifiant de compte utilisateur, **When** l'administrateur le suspend ou le réactive, **Then** une confirmation s'affiche.
3. **Given** un identifiant inexistant, **When** une action de modération est soumise, **Then** un message explicite ("introuvable") s'affiche.

### User Story 4 - Suivi et tarification des abonnements (Priority: P2)
Un administrateur consulte les abonnements par statut, déclenche une relance sur un impayé, et
ajuste le tarif standard par périodicité.

**Independent Test**: Lister les abonnements `IMPAYE`, déclencher une relance ; modifier le tarif `ANNUEL`.

**Acceptance Scenarios**:
1. **Given** le tableau de bord abonnements, **When** l'administrateur filtre par statut, **Then** seuls les abonnements de ce statut apparaissent.
2. **Given** un abonnement `IMPAYE`, **When** l'administrateur déclenche une relance, **Then** le résultat réel (notification envoyée ou non) s'affiche.
3. **Given** le tarif standard d'une périodicité, **When** l'administrateur le modifie, **Then** la nouvelle valeur s'affiche immédiatement.

### Edge Cases
- Clé API admin non configurée côté serveur (`ADMIN_API_KEY` absente) : toute action échoue avec le
  même message d'erreur explicite que pour une clé incorrecte (l'API ne distingue pas les deux cas).
- API indisponible : message d'erreur explicite sur chaque section, jamais un écran vide (repris de 010/011).

## Requirements *(mandatory)*
- **FR-001**: Le frontend MUST permettre à un administrateur de s'identifier via la clé API admin, conservée pour les requêtes suivantes.
- **FR-002**: Le frontend MUST permettre de lister les dossiers KYC par statut, d'en consulter le détail, de les valider ou de les rejeter, en consommant `AdminController` (existant, 004).
- **FR-003**: Le frontend MUST permettre de suspendre ou réactiver un compte utilisateur ou un établissement par identifiant, en consommant `ModerationController` (existant, 009).
- **FR-004**: Le frontend MUST permettre de lister les abonnements par statut, déclencher une relance, consulter et modifier le tarif standard, en consommant `BillingController` (existant, 008).
- **FR-005**: Le frontend MUST afficher un message d'erreur explicite (jamais un écran vide) sur toute erreur API, y compris `401` (clé invalide) et `409` (dossier incomplet).

### Key Entities
Aucune nouvelle entité, aucun nouvel endpoint : ce frontend consomme intégralement l'API admin déjà
implémentée (features 004, 008, 009).

## Success Criteria *(mandatory)*
- **SC-001**: Un administrateur peut passer de "connexion" à "dossier validé" sans quitter l'application.
- **SC-002**: 100% des erreurs API (4xx/5xx) affichent un message explicite, jamais une page blanche.
- **SC-003**: 100% des actions de modération par identifiant inexistant affichent "introuvable" sans erreur technique visible.

## Assumptions
- **Session admin** : la clé API est conservée en `ProtectedLocalStorage` (même mécanisme que les
  sessions client/gérant, 010/011), envoyée en en-tête `X-Admin-Api-Key` sur chaque requête admin —
  cohérent avec la dette technique déjà documentée côté API (pas de vraie authentification admin).
- **Modération par identifiant** : la page de modération accepte un identifiant saisi manuellement
  (ou copié depuis la liste KYC pour les établissements) plutôt qu'une recherche/liste dédiée des
  comptes clients — aucun endpoint de recherche de comptes n'existe côté API pour cette itération.
- **Design** : composants Blazor par défaut (Bootstrap du template), même choix que 010/011.
