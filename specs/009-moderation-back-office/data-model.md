# Data Model: Modération back-office

## Utilisateur (existant, `docs/merise/05-mpd.sql`) — attribut ajouté
| Champ | Type | Contrainte |
|---|---|---|
| est_suspendu | boolean | NOT NULL, défaut `false` (nouveau, migration 0006) |

**Règle**: `est_suspendu = true` MUST bloquer toute nouvelle demande OTP (FR-005).

## Etablissement (existant, `docs/merise/05-mpd.sql`) — attribut ajouté
| Champ | Type | Contrainte |
|---|---|---|
| est_suspendu | boolean | NOT NULL, défaut `false` (nouveau, migration 0006) |

**Règle**: `est_suspendu = true` MUST exclure l'établissement de la recherche/fiche annuaire
(FR-006), bloquer toute nouvelle demande de RDV (FR-007) et toute nouvelle souscription
d'abonnement (FR-008). Orthogonal à `statut_kyc` (voir research.md Décision 1) : aucune
interaction entre les deux champs.

## Relations
Aucune nouvelle relation — extension d'attribut sur des entités déjà modélisées.
