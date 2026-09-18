# Phase 0 — Research: Gestion des Prestations et Catégories par le Partenaire

## Décision 1 — Généraliser RequestOtpUseCase/VerifyOtpUseCase au type de compte
- **Decision**: Les deux use cases (003) acceptent un paramètre `TypeCompte` (défaut `CLIENT` pour
  compatibilité avec l'API existante), au lieu de la constante `"CLIENT"` codée en dur.
- **Rationale**: Réutilise 100% de la logique OTP déjà testée (hachage, expiration, invalidation) sans
  dupliquer un second flux OTP pour les partenaires (DRY).
- **Alternatives rejetées**: Dupliquer un `RequestPartnerOtpUseCase` distinct — inutile, la seule
  différence est le type de compte cible.

## Décision 2 — Vérification de propriété via en-tête `X-Partner-Id`
- **Decision**: `PartnerOwnershipFilter` lit l'en-tête `X-Partner-Id` (GUID = `idUtilisateur` obtenu
  après OTP), le compare à `etablissement.id_utilisateur_gerant` de la ressource ciblée ; refuse
  (`403`) si absent ou différent.
- **Rationale**: Cohérent avec le compromis déjà accepté (clé admin statique, features 004/005) —
  pas de session/JWT construite à ce stade (spec, Assumptions). Documenté comme dette technique.
- **Alternatives rejetées**: Construire une session/JWT complète — disproportionné pour cette feature
  seule (YAGNI), à traiter dans une feature de session dédiée future.

Prêt pour la Phase 1.
