# Feature Specification: Frontend client — annuaire et prise de RDV

**Feature Branch**: `010-frontend-client-annuaire-rdv`
**Created**: 2026-09-18
**Status**: Draft
**Input**: User description: "Démarrer la construction du frontend. Web + mobile à terme avec un seul framework .NET, en commençant par une application web Blazor. Premier parcours : le client (recherche annuaire géolocalisée + prise de RDV), correspondant aux User Stories déjà implémentées côté API (US-03, US-06 à US-09, US-12, US-14)."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Recherche et consultation de l'annuaire (Priority: P1)
Un visiteur (client, non authentifié) recherche des établissements par catégorie/commune et consulte leur fiche.

**Independent Test**: Ouvrir la page d'accueil, filtrer par catégorie et/ou commune, ouvrir une fiche établissement.

**Acceptance Scenarios**:
1. **Given** la page d'accueil, **When** le visiteur choisit une catégorie et/ou une commune, **Then** la liste affiche uniquement les établissements correspondants (US-06).
2. **Given** une fiche établissement, **When** elle s'affiche, **Then** elle montre médias, prestations et tarifs (US-07).
3. **Given** une fiche établissement, **When** le visiteur clique sur le numéro de service client, **Then** l'action d'appel du terminal est déclenchée (US-08, `tel:`).
4. **Given** une fiche établissement, **When** le visiteur clique sur "itinéraire", **Then** un lien externe vers Yango/Google Maps/Apple Maps s'ouvre avec les coordonnées GPS (US-09).
5. **Given** aucun établissement ne correspond aux filtres, **When** la recherche s'exécute, **Then** un message "aucun résultat" s'affiche (pas d'erreur).

### User Story 2 - Authentification du client par OTP WhatsApp (Priority: P1)
Un client s'authentifie par numéro de téléphone + code OTP pour pouvoir prendre RDV.

**Independent Test**: Saisir un numéro, recevoir/saisir le code, obtenir une session locale.

**Acceptance Scenarios**:
1. **Given** un numéro de téléphone valide, **When** le client demande un code, **Then** le système confirme l'envoi (ou affiche l'échec explicite si Zavu non configuré — US-03).
2. **Given** un code reçu, **When** le client le saisit correctement, **Then** il est authentifié et son identifiant est conservé pour les actions suivantes (RDV).
3. **Given** un code invalide ou expiré, **When** le client le soumet, **Then** un message d'erreur explicite s'affiche, sans bloquer une nouvelle tentative.

### User Story 3 - Prise de rendez-vous (Priority: P1)
Un client authentifié demande un RDV sur un créneau libre d'un établissement, et voit le statut de sa demande.

**Independent Test**: Depuis une fiche établissement, choisir une prestation et un créneau libre, soumettre la demande.

**Acceptance Scenarios**:
1. **Given** un client authentifié sur une fiche établissement, **When** il choisit une prestation et un créneau libre, **Then** la demande de RDV est envoyée et son statut ("Demandé") s'affiche (US-12).
2. **Given** un créneau déjà occupé, **When** le calendrier s'affiche, **Then** ce créneau apparaît indisponible/en rouge et n'est pas sélectionnable (US-12).
3. **Given** une demande de RDV envoyée, **When** le gérant confirme/refuse (côté API), **Then** le client voit le statut mis à jour à son prochain accès à la page (US-14 ; pas de notification push dans ce périmètre — voir Assumptions).
4. **Given** un client non authentifié, **When** il tente de prendre RDV, **Then** il est redirigé vers l'authentification OTP.

### Edge Cases
- Établissement suspendu ou non validé : n'apparaît jamais dans la recherche (le frontend ne fait que refléter l'API existante, FR-006 de 009).
- Perte de la session locale (OTP) : le client doit se réauthentifier pour toute nouvelle action nécessitant son identité.
- API backend indisponible : chaque page affiche un message d'erreur explicite plutôt qu'un écran vide silencieux.

## Requirements *(mandatory)*
- **FR-001**: Le frontend MUST permettre la recherche d'établissements par catégorie et/ou commune, en consommant `GET /etablissements` (005).
- **FR-002**: Le frontend MUST afficher la fiche détaillée d'un établissement (médias, prestations, tarifs), en consommant `GET /etablissements/{id}` (005).
- **FR-003**: Le frontend MUST proposer un appel direct (lien `tel:`) et un lien d'itinéraire externe (Yango/Google Maps/Apple Maps) depuis la fiche établissement.
- **FR-004**: Le frontend MUST permettre à un client de demander un code OTP et de le vérifier, en consommant `POST /auth/otp/request` et `POST /auth/otp/verify` (003).
- **FR-005**: Le frontend MUST conserver l'identifiant client (`idUtilisateur`) obtenu après vérification OTP pour l'utiliser comme preuve d'identité (`X-Client-Id`) sur les actions suivantes, cohérent avec la dette technique déjà documentée côté API (pas de session/JWT).
- **FR-006**: Le frontend MUST afficher les créneaux occupés d'un établissement pour une date donnée (`GET /etablissements/{id}/creneaux`) et empêcher visuellement leur sélection (007).
- **FR-007**: Le frontend MUST permettre à un client authentifié de soumettre une demande de RDV (`POST /rdv`) et d'afficher le résultat (créé, conflit de créneau, erreur).
- **FR-008**: Le frontend MUST afficher un message d'erreur explicite (jamais un écran vide) lorsque l'API backend est indisponible ou renvoie une erreur.

### Key Entities
Aucune nouvelle entité : le frontend consomme les entités déjà modélisées et exposées par l'API
(Etablissement, Prestation, Media, Rdv, Utilisateur/Client).

## Success Criteria *(mandatory)*
- **SC-001**: Un visiteur peut aller de la page d'accueil à l'affichage d'une fiche établissement en 3 clics ou moins.
- **SC-002**: 100% des créneaux occupés retournés par l'API apparaissent visuellement indisponibles dans le calendrier.
- **SC-003**: 100% des erreurs API (4xx/5xx) affichent un message explicite à l'utilisateur, jamais une page blanche.
- **SC-004**: Un client peut passer de "numéro de téléphone saisi" à "RDV demandé" sans quitter l'application (pas de rechargement de page complet nécessaire au-delà de la navigation normale).

## Assumptions
- **Framework** : Blazor Web App (.NET 8, mode d'interactivité Server) — un seul projet web pour
  commencer ; la base mobile (.NET MAUI Blazor Hybrid) réutilisera les mêmes composants Razor dans
  une itération ultérieure (choix explicite de l'utilisateur : Web d'abord).
- **Session client** : pas de vraie authentification/session (JWT, cookies sécurisés) dans ce
  périmètre — l'identifiant client obtenu après OTP est conservé côté navigateur (ex. stockage
  local du navigateur), cohérent avec la dette technique déjà documentée côté API (`X-Client-Id`).
  Une vraie feature d'authentification remplacera ce mécanisme plus tard (comme prévu côté API).
- **Notifications RDV** : pas de push/WebSocket dans ce périmètre ; le client voit le statut à jour
  en rechargeant/revisitant la page (US-14 côté API reste la notification WhatsApp existante,
  indépendante du frontend).
- **Design** : composants Blazor par défaut (Bootstrap du template), sans design system spécifique
  pour cette première itération — l'apparence sera raffinée dans une itération ultérieure dédiée.
- **Parcours partenaire et admin** : hors périmètre de cette feature (feront l'objet de features
  frontend dédiées ultérieures, comme convenu avec l'utilisateur).
