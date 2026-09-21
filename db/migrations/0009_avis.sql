-- Migration 0009 : avis clients apres rendez-vous termine (feature 014).

CREATE TABLE avis (
    id_avis UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    id_rdv UUID NOT NULL UNIQUE REFERENCES rdv(id_rdv) ON DELETE CASCADE,
    note SMALLINT NOT NULL CHECK (note BETWEEN 1 AND 5),
    commentaire TEXT,
    date_creation TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_avis_rdv ON avis(id_rdv);
