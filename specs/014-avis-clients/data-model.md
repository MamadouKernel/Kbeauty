# Data Model & Contracts: Avis clients après rendez-vous

## Nouvelle entité : `avis`

| Champ | Type | Contraintes |
|---|---|---|
| `id_avis` | UUID | PK, `gen_random_uuid()` |
| `id_rdv` | UUID | NOT NULL, UNIQUE, FK → `rdv(id_rdv)` ON DELETE CASCADE |
| `note` | SMALLINT | NOT NULL, CHECK (note BETWEEN 1 AND 5) |
| `commentaire` | TEXT | NULLABLE |
| `date_creation` | TIMESTAMPTZ | NOT NULL DEFAULT now() |

`id_utilisateur_client` et `id_etablissement` ne sont pas dupliqués : toujours retrouvés via
`JOIN rdv` (FR de normalisation — évite toute incohérence si jamais un RDV changeait d'établissement,
ce qui n'arrive pas aujourd'hui mais reste la bonne pratique MERISE).

## Migration `db/migrations/0009_avis.sql`

```sql
CREATE TABLE avis (
    id_avis UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    id_rdv UUID NOT NULL UNIQUE REFERENCES rdv(id_rdv) ON DELETE CASCADE,
    note SMALLINT NOT NULL CHECK (note BETWEEN 1 AND 5),
    commentaire TEXT,
    date_creation TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_avis_rdv ON avis(id_rdv);
```

## Contrats API

### `POST /rdv/{id}/avis`
Headers: `X-Client-Id`. Body: `{ "note": 5, "commentaire": "Super accueil !" }`.
- 201 : `{ "idAvis": "guid" }`
- 400 : `{ "status": "note_invalide" }` (hors 1-5)
- 401 : en-tête absent/invalide
- 404 : RDV introuvable, n'appartient pas au client, ou statut ≠ TERMINE (une seule réponse pour les
  trois cas, pas de fuite d'info — FR-004)
- 409 : `{ "status": "avis_deja_existant" }`

### `GET /etablissements/{id}/avis`
Public, pas d'en-tête requis.
- 200 : `{ "noteMoyenne": 4.5, "nombreAvis": 2, "avis": [ { "note": 5, "commentaire": "...", "dateCreation": "..." }, ... ] }`
  triés du plus récent au plus ancien. `noteMoyenne: null, avis: []` si aucun avis (FR-006, jamais de
  valeur inventée).

### `GET /rdv` (013, étendu)
Chaque élément gagne un champ `aDejaAvis: boolean` (true seulement si `StatutRdv == "TERMINE"` et une
ligne `avis` existe pour ce RDV).
