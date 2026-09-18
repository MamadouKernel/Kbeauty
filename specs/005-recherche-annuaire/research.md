# Phase 0 — Research: Recherche et Consultation de l'Annuaire par le Client

## Décision 1 — Exclusion des non-validés/sans-catégorie via INNER JOIN, pas de filtre applicatif séparé

- **Decision**: La requête de recherche fait un `INNER JOIN etablissement_categorie` sur
  `etablissement`, avec `WHERE statut_kyc = 'VALIDE'`. Un établissement sans aucune ligne dans
  `etablissement_categorie` est automatiquement exclu par la nature de l'`INNER JOIN` (FR-002).
- **Rationale**: Garantit FR-002 au niveau de la requête elle-même, sans logique applicative
  supplémentaire pouvant diverger (DRY, une seule source de vérité pour la règle).
- **Alternatives envisagées**: `LEFT JOIN` + filtre applicatif "au moins une catégorie" en C# — rejeté
  (plus de code, même résultat, risque d'oubli si la requête change).

## Décision 2 — Réponse identique pour "non trouvé" et "non validé" (FR-007)

- **Decision**: `GetEtablissementDetailUseCase` retourne `null` aussi bien si l'établissement
  n'existe pas que s'il existe mais n'est pas `VALIDE` ; le contrôleur retourne alors `404` dans les
  deux cas, avec le même corps de réponse vide.
- **Rationale**: Répond littéralement à FR-007 et à l'Edge Case correspondant — ne jamais révéler
  qu'un identifiant correspond à un dossier en attente/rejeté.
- **Alternatives envisagées**: Distinguer `403` (existe mais non validé) de `404` (inexistant) —
  rejeté explicitement, car cela permettrait de déduire l'existence d'un dossier non public.

## Décision 3 — Assignation catégorie par "find-or-create" sur le libellé, pas de seed

- **Decision**: `ICategorieRepository.FindOrCreateByLibelleAsync(libelle)` utilise
  `INSERT ... ON CONFLICT (libelle_categorie) DO NOTHING` puis relit la ligne — pas de migration de
  seed de catégories.
- **Rationale**: Contrairement au référentiel géographique (feature 004, dépendance bloquante réelle
  car `id_commune` est une FK NOT NULL), `categorie` n'a pas cette contrainte structurelle : une
  catégorie peut être créée à la volée sans violer le MPD. Find-or-create évite une migration
  superflue (YAGNI) tout en restant idempotent.
- **Alternatives envisagées**: Seed migration des 4 catégories du CDC (coiffure/spa/massage/onglerie)
  — envisageable mais rejeté ici car le mécanisme FR-008 est explicitement temporaire (spec,
  Assumptions) ; find-or-create suffit et sera remplacé par une vraie gestion partenaire.

## Décision 4 — Endpoints FR-008 rattachés à `AdminController` existant, pas de nouveau contrôleur

- **Decision**: Les deux endpoints minimaux d'assignation (`POST .../categories`,
  `POST .../prestations`) sont ajoutés à `AdminController` (feature 004), sous le même
  `[ServiceFilter(typeof(AdminApiKeyFilter))]`.
- **Rationale**: Réutilise la protection déjà en place et déjà testée (feature 004) plutôt que de
  dupliquer un mécanisme de clé — cohérent avec la spec (Assumptions : "réutilisée par cohérence").
- **Alternatives envisagées**: Nouveau contrôleur dédié avec sa propre protection — rejeté, dupliquerait
  sans bénéfice une protection déjà fonctionnelle.

## Décision 5 — Lien d'itinéraire Google Maps, format URL simple

- **Decision**: `https://www.google.com/maps/dir/?api=1&destination={lat},{long}`, généré côté
  backend et renvoyé comme champ `lienItineraire` dans la fiche établissement.
- **Rationale**: Format d'URL Google Maps standard, ne nécessite aucune clé API (contrairement à
  l'API Google Maps Platform, qui reste un compte tiers non créé à ce stade — cf. tableau des comptes
  à créer déjà transmis). Suffisant pour répondre à FR-005 sans nouvelle dépendance externe.
- **Alternatives envisagées**: Intégration de l'API Google Maps Platform (calcul d'itinéraire réel) —
  rejeté pour cette itération (nécessite un compte/clé non encore créé, hors périmètre backend actuel).

Toutes les inconnues techniques sont résolues ; prêt pour la Phase 1 (Design & Contracts).
