# Research: Frontend partenaire — gestion établissement et RDV

## Décision 1 — Deux endpoints minimaux ajoutés plutôt qu'une refonte
**Décision**: `GET /partenaire/etablissements` (liste "mes établissements" par `X-Partner-Id`) et
`GET /partenaire/etablissements/{id}/rdv` (liste des RDV, protégé par `PartnerOwnershipFilter`
existant).
**Rationale**: aucun des deux n'existe aujourd'hui (vérifié par recherche dans le code) — sans eux,
un gérant n'a aucun moyen de découvrir son établissement ni ses demandes de RDV après connexion.
Extension minimale et cohérente avec les patterns déjà en place (mêmes filtres d'autorisation).
**Alternatives rejetées**: exiger que le gérant connaisse déjà l'id de son établissement (aucune
UX viable) ; construire un tableau de bord complet avec pagination/filtrage avancé (hors périmètre,
YAGNI pour cette première itération).

## Décision 2 — Réutilisation du `PartnerOwnershipFilter` pour la liste RDV
**Décision**: `GET /partenaire/etablissements/{id}/rdv` protégé par le même filtre que les
décisions RDV existantes (`PartnerOwnershipFilter`), pas un nouveau mécanisme.
**Rationale**: cohérence avec `PartnerManagementController`/`RdvController` déjà en place ; le
filtre vérifie déjà `id_utilisateur_gerant` via `IPartnerPrestationRepository.GetOwnerIdAsync`.
**Alternatives rejetées**: dupliquer la logique de vérification directement dans le contrôleur.

## Décision 3 — Abonnement hors périmètre de cette itération
**Décision**: le parcours d'abonnement/paiement (déjà complet côté API, WiniPayer) n'est pas
intégré au frontend partenaire dans cette itération.
**Rationale**: complexité propre (redirection vers checkout externe, retour, réconciliation) qui
mérite sa propre itération frontend plutôt que d'alourdir celle-ci (voir spec.md Assumptions).
**Alternatives rejetées**: tout inclure d'un coup — risque de retarder la livraison des besoins les
plus immédiats (gestion établissement, RDV).
