# Feature Specification: Recherche et Consultation de l'Annuaire par le Client

**Feature Branch**: `005-recherche-annuaire`

**Created**: 2026-09-18

**Status**: Draft

**Input**: User description: "Recherche et consultation de l'annuaire par le client (features US-06 à US-09 du Product Backlog). Le système doit permettre à un client de rechercher des établissements de beauté par catégorie (salon de coiffure, spa, massage, onglerie) et par localisation (au minimum la commune), et de ne voir apparaître dans les résultats que les établissements dont le dossier partenaire a été validé par un administrateur (statut VALIDE, règle déjà appliquée par la feature 004-onboarding-partenaire) et qui ont au moins une catégorie assignée. Le client doit pouvoir consulter la fiche complète d'un établissement : nom, description, médias (photos/vidéos), liste des prestations avec leurs tarifs, numéro de service client avec une action pour appeler, et un lien d'itinéraire (Google Maps) vers les coordonnées GPS de l'établissement. Cette feature s'appuie sur les entités déjà modélisées en MERISE (Etablissement, Categorie, Commune, Media, Prestation) et sur le squelette applicatif (002-scaffold-dotnet). Comme aucune fonctionnalité de gestion des prestations et catégories par le partenaire n'existe encore (features futures), cette feature doit inclure un minimum d'endpoints internes ou de données de test permettant d'assigner une catégorie et une prestation à un établissement existant, uniquement pour pouvoir valider que la recherche fonctionne de bout en bout."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Recherche par catégorie et localisation (Priority: P1)

Un client recherche des établissements de beauté en filtrant par catégorie (ex. Spa) et par commune,
afin de trouver des options pertinentes à proximité.

**Why this priority**: C'est le point d'entrée principal du parcours client dans l'annuaire (CDC §3.1)
— sans recherche, aucun établissement n'est jamais découvert.

**Independent Test**: Peut être testé seul en filtrant sur une catégorie et une commune données, et
en vérifiant que seuls les établissements correspondants et éligibles apparaissent.

**Acceptance Scenarios**:

1. **Given** plusieurs établissements validés avec des catégories et communes différentes, **When**
   un client filtre par une catégorie et une commune précises, **Then** seuls les établissements
   validés correspondant aux deux critères apparaissent.
2. **Given** un établissement validé mais sans aucune catégorie assignée, **When** un client effectue
   une recherche qui correspondrait par ailleurs à cet établissement (localisation), **Then** il
   n'apparaît pas dans les résultats.
3. **Given** aucun établissement ne correspond aux critères, **When** le client effectue la recherche,
   **Then** il reçoit une liste vide, pas une erreur.

---

### User Story 2 - Exclusion des établissements non validés (Priority: P1)

Un client ne doit jamais voir, dans ses résultats de recherche, un établissement dont le dossier
partenaire est en attente ou a été rejeté.

**Why this priority**: Protège l'intégrité de l'annuaire (CDC §5, RG-ETB-01) — un établissement non
vérifié ne doit jamais être visible, quelle que soit la recherche effectuée.

**Independent Test**: Peut être testé en comparant les résultats d'une recherche avant et après la
validation d'un établissement donné (dépendance déjà couverte par la feature 004).

**Acceptance Scenarios**:

1. **Given** un établissement au statut "en attente" ou "rejeté" correspondant par ailleurs aux
   critères de recherche, **When** un client effectue cette recherche, **Then** cet établissement
   n'apparaît jamais dans les résultats.

---

### User Story 3 - Consultation de la fiche établissement (Priority: P1)

Un client consulte la fiche complète d'un établissement validé pour voir sa description, ses médias,
ses prestations et tarifs, et pour pouvoir le contacter ou s'y rendre.

**Why this priority**: C'est l'objectif final de la recherche — sans fiche consultable, la recherche
n'a pas de valeur pour le client (CDC §3.1).

**Independent Test**: Peut être testé seul en consultant la fiche d'un établissement validé déjà
connu, et en vérifiant la présence de chaque élément attendu.

**Acceptance Scenarios**:

1. **Given** un établissement validé avec des médias et des prestations, **When** un client consulte
   sa fiche, **Then** il voit le nom, la description, les médias, la liste des prestations avec leurs
   tarifs, et le numéro de service client.
2. **Given** cette fiche affichée, **When** le client déclenche l'action d'appel, **Then** le numéro
   de service client de l'établissement est utilisé.
3. **Given** cette fiche affichée, **When** le client déclenche l'action d'itinéraire, **Then** un
   lien d'itinéraire est généré à partir des coordonnées GPS exactes de l'établissement.
4. **Given** un établissement non validé, **When** un client tente de consulter sa fiche directement
   (par un identifiant connu), **Then** l'accès est refusé, cohérent avec son absence des résultats
   de recherche (US2).

---

### User Story 4 - Données minimales de test pour la recherche (Priority: P2)

Une personne technique doit pouvoir assigner au moins une catégorie et une prestation à un
établissement déjà validé, afin de pouvoir tester la recherche de bout en bout, en l'absence de toute
fonctionnalité de gestion des prestations/catégories par le partenaire lui-même (hors périmètre de
cette feature, prévue dans une feature future).

**Why this priority**: Nécessaire pour valider les autres User Stories, mais n'apporte aucune valeur
client directe — priorité la plus basse.

**Independent Test**: Peut être testé seul en assignant une catégorie et une prestation à un
établissement, puis en vérifiant qu'il devient trouvable par cette catégorie (US1).

**Acceptance Scenarios**:

1. **Given** un établissement validé sans catégorie ni prestation, **When** une catégorie et une
   prestation lui sont assignées via ce mécanisme minimal, **Then** il devient trouvable par cette
   catégorie et sa prestation apparaît sur sa fiche.

---

### Edge Cases

- Que se passe-t-il si un client recherche sans préciser de catégorie, uniquement une commune ? →
  Tous les établissements validés de cette commune (avec au moins une catégorie) apparaissent.
- Que se passe-t-il si un établissement a plusieurs catégories ? → Il apparaît dans les résultats de
  recherche pour chacune de ses catégories.
- Comment le système réagit-il si l'identifiant d'établissement consulté n'existe pas du tout (pas
  seulement non validé) ? → Une réponse "non trouvé" distincte, mais sans révéler si l'établissement
  existe et n'est simplement pas validé, ou n'existe pas du tout (comportement identique dans les deux
  cas, pour ne pas fuiter d'information sur les dossiers en attente).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Le système MUST permettre à un client de rechercher des établissements en filtrant par
  catégorie et/ou par commune.
- **FR-002**: Un établissement ne MUST apparaître dans les résultats de recherche que s'il est à la
  fois au statut "validé" (RG-ETB-01) ET rattaché à au moins une catégorie.
- **FR-003**: Une recherche sans résultat MUST retourner une liste vide, jamais une erreur.
- **FR-004**: Le système MUST permettre à un client de consulter la fiche complète d'un établissement
  validé : nom, description, médias, liste des prestations avec tarifs, numéro de service client.
- **FR-005**: Le système MUST fournir, sur la fiche établissement, une action de génération de lien
  d'itinéraire à partir des coordonnées GPS exactes de l'établissement.
- **FR-006**: Le système MUST fournir, sur la fiche établissement, le numéro de service client dans
  un format exploitable pour déclencher un appel.
- **FR-007**: Le système MUST refuser l'accès à la fiche d'un établissement non validé, avec la même
  réponse que pour un établissement inexistant (Edge Case — pas de fuite d'information).
- **FR-008**: Le système MUST fournir un mécanisme minimal (interne, non destiné aux clients finaux)
  permettant d'assigner une catégorie et une prestation à un établissement déjà validé, en l'absence
  de toute fonctionnalité de gestion partenaire équivalente à ce stade du projet.

### Key Entities *(include if feature involves data)*

- **Établissement** : entité déjà modélisée — cette feature lit son statut, ses catégories, médias,
  prestations, sans modifier sa structure.
- **Catégorie**, **Prestation**, **Média**, **Commune** : entités déjà modélisées, lues et (pour
  Catégorie/Prestation) associées à un établissement via le mécanisme minimal de FR-008.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Un client peut passer d'une recherche par catégorie/commune à la fiche complète d'un
  établissement en 2 étapes maximum (recherche → sélection).
- **SC-002**: 100% des établissements non validés ou sans catégorie sont absents des résultats de
  recherche (0 fuite constatée en test, cohérent avec la feature 004).
- **SC-003**: 100% des tentatives d'accès direct à la fiche d'un établissement non validé sont
  refusées avec la même réponse qu'un établissement inexistant.
- **SC-004**: Chaque fiche établissement validée et complète expose toutes les informations requises
  (nom, description, médias, prestations/tarifs, contact, itinéraire) sans étape supplémentaire.

## Assumptions

- Le "lien d'itinéraire" généré cible Google Maps par défaut (cohérent avec le CDC qui mentionne
  également Yango/Apple Maps comme alternatives futures, hors périmètre de cette itération).
- L'action d'appel se limite à exposer le numéro dans un format standard (ex. E.164) ; le
  déclenchement effectif de l'appel dépend du client final (application mobile/web), hors périmètre
  backend de cette feature.
- Le mécanisme minimal d'assignation catégorie/prestation (FR-008) est un outil technique temporaire,
  sans contrôle d'accès dédié au-delà de ce qui existe déjà (protection admin de la feature 004,
  réutilisée par cohérence) ; il sera remplacé par une véritable gestion partenaire dans une feature
  future du Product Backlog (Epic "Établissement").
- La recherche ne couvre pas la pagination, le tri par pertinence/distance ni la recherche textuelle
  libre — hors périmètre de cette itération (US-06/US-07 se limitent au filtrage par catégorie/commune).
