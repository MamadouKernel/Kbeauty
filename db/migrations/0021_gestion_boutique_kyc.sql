ALTER TABLE etablissement
    ADD COLUMN IF NOT EXISTS motif_rejet TEXT,
    ADD COLUMN IF NOT EXISTS date_soumission_kyc TIMESTAMPTZ NOT NULL DEFAULT now();

CREATE TABLE IF NOT EXISTS indisponibilite_etablissement (
    id_indisponibilite UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    id_etablissement UUID NOT NULL REFERENCES etablissement(id_etablissement) ON DELETE CASCADE,
    date_debut TIMESTAMPTZ NOT NULL,
    date_fin TIMESTAMPTZ NOT NULL,
    motif VARCHAR(150),
    CHECK (date_fin > date_debut)
);

CREATE INDEX IF NOT EXISTS idx_indisponibilite_etablissement_dates
    ON indisponibilite_etablissement(id_etablissement, date_debut, date_fin);
