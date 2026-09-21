# Data Model: Historique des rendez-vous client et paiement en ligne

## Entité existante : `rdv` (inchangée)
Aucune colonne ajoutée. Rappel des champs utilisés par cette feature :
`id_rdv, date_heure_debut, statut_rdv, id_utilisateur_client, id_etablissement, id_prestation`.

## Nouvelle entité : `transaction_rdv`

| Champ | Type | Contraintes | Notes |
|---|---|---|---|
| `id_transaction_rdv` | UUID | PK, `gen_random_uuid()` | |
| `id_rdv` | UUID | NOT NULL, UNIQUE, FK → `rdv(id_rdv)` ON DELETE CASCADE | Un seul essai de paiement en ligne par RDV (FR-009) |
| `montant` | NUMERIC(10,2) | NOT NULL, CHECK > 0 | Copié du tarif de la prestation au moment de l'initiation |
| `statut_transaction` | `statut_transaction_enum` (réutilisé) | NOT NULL DEFAULT 'EN_COURS' | Valeurs existantes : EN_COURS / REUSSIE / ECHOUEE / REMBOURSEE |
| `canal_paiement` | `canal_paiement_enum` (réutilisé), NULLABLE | — | NULL car WiniPayer checkout hébergé ne demande pas le canal à l'avance (même choix que 0007) |
| `operateur_externe` | VARCHAR(50) | NULLABLE | Renseigné par le callback (ex. "wave-cote-divoire") |
| `reference_externe` | VARCHAR(64) | NULLABLE, indexé | UUID de facture WiniPayer, utilisé pour réconcilier le callback |
| `date_transaction` | TIMESTAMPTZ | NOT NULL DEFAULT now() | |
| `date_maj` | TIMESTAMPTZ | NOT NULL DEFAULT now() | Mise à jour à chaque changement de statut (utile pour FR-008, détecter une attente prolongée) |

**Invariants**:
- FR-009 (idempotence) : contrainte `UNIQUE(id_rdv)` — une deuxième tentative de paiement en ligne pour
  le même RDV réutilise la ligne existante plutôt que d'en insérer une nouvelle.
- FR-010 (séparation des statuts) : aucune contrainte ni trigger ne lie `transaction_rdv.statut_transaction`
  à `rdv.statut_rdv` — la mise à jour de l'un ne touche jamais l'autre, appliqué au niveau du code
  (les deux use cases n'écrivent chacun que dans leur propre table).

## Vue de lecture : Historique RDV client (pas de nouvelle table)

Requête portée par `RdvRepository.ListByClientAsync(idClient)` :

```
rdv
  JOIN etablissement ON rdv.id_etablissement = etablissement.id_etablissement
  JOIN prestation    ON rdv.id_prestation    = prestation.id_prestation
  LEFT JOIN transaction_rdv ON transaction_rdv.id_rdv = rdv.id_rdv
WHERE rdv.id_utilisateur_client = @idClient
ORDER BY rdv.date_heure_debut DESC
```

Projection retournée à l'API (DTO `RdvHistoriqueItem`) :
`idRdv, nomEtablissement, libellePrestation, dateHeureDebut, statutRdv, statutPaiement (nullable — null
si aucun paiement en ligne n'a été tenté, sinon EN_COURS/REUSSIE/ECHOUEE)`.

## Migration

`db/migrations/0008_transaction_rdv.sql` :

```sql
CREATE TABLE transaction_rdv (
    id_transaction_rdv UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    id_rdv UUID NOT NULL UNIQUE REFERENCES rdv(id_rdv) ON DELETE CASCADE,
    montant NUMERIC(10,2) NOT NULL CHECK (montant > 0),
    statut_transaction statut_transaction_enum NOT NULL DEFAULT 'EN_COURS',
    canal_paiement canal_paiement_enum,
    operateur_externe VARCHAR(50),
    reference_externe VARCHAR(64),
    date_transaction TIMESTAMPTZ NOT NULL DEFAULT now(),
    date_maj TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_transaction_rdv_reference_externe ON transaction_rdv(reference_externe);
```

(`statut_transaction_enum` et `canal_paiement_enum` existent déjà depuis `0001_init_schema.sql` — pas
de nouveau type à créer.)
