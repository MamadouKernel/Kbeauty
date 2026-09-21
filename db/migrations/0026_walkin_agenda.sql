ALTER TABLE rdv ADD COLUMN IF NOT EXISTS origine_rdv VARCHAR(20) NOT NULL DEFAULT 'EN_LIGNE' CHECK (origine_rdv IN ('EN_LIGNE','COMPTOIR'));
CREATE INDEX IF NOT EXISTS idx_rdv_agenda ON rdv(id_etablissement, date_heure_debut, id_collaborateur);
