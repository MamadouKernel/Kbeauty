# Data Model: Abonnement et Paiement

## Abonnement (existant, `docs/merise/05-mpd.sql`)
| Champ | Type | Contrainte |
|---|---|---|
| id_abonnement | uuid | PK |
| id_etablissement | uuid | FK établissement |
| periodicite | periodicite_enum (MENSUEL/ANNUEL) | NOT NULL |
| montant | numeric | NOT NULL — copié du tarif standard au moment de la souscription |
| statut | statut_abonnement_enum (ACTIF/IMPAYE/RESILIE) | NOT NULL |
| date_debut | timestamptz | NOT NULL |
| date_fin_engagement | timestamptz | NOT NULL — date_debut + 1 an (RG-ABO-02/03) |

**Règle**: un seul `abonnement` avec `statut = ACTIF` par `id_etablissement` (FR-004), vérifié
atomiquement à l'insertion (cf. research.md Décision 2).

## Transaction (existant, `docs/merise/05-mpd.sql`)
| Champ | Type | Contrainte |
|---|---|---|
| id_transaction | uuid | PK |
| id_abonnement | uuid | FK abonnement |
| canal | canal_paiement_enum (WAVE/ORANGE_MONEY/MTN/MOOV/VISA/MASTERCARD) | NOT NULL |
| statut | statut_transaction_enum (EN_COURS/REUSSIE/ECHOUEE/REMBOURSEE) | NOT NULL |
| montant | numeric | NOT NULL |
| date_transaction | timestamptz | NOT NULL |

## ParametreAbonnement (nouveau — configuration, pas une entité MERISE métier)
| Champ | Type | Contrainte |
|---|---|---|
| periodicite | periodicite_enum | PK |
| montant | numeric | NOT NULL |

**Règle**: modifiable par l'admin (FR-007) ; lu en lecture seule au moment de chaque souscription ;
n'affecte jamais les abonnements déjà créés (le montant leur est déjà figé).

## Relations
- `Etablissement (1) --- (0..n) Abonnement` (un établissement peut avoir plusieurs abonnements dans
  le temps, mais au plus un `ACTIF`).
- `Abonnement (1) --- (0..n) Transaction` (une tentative de paiement par souscription, plus les
  éventuelles relances/tentatives ultérieures si repaiement).
