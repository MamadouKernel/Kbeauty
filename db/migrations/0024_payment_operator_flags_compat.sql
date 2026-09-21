ALTER TABLE etablissement
    ADD COLUMN IF NOT EXISTS paiement_wave BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS paiement_orange_money BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS paiement_moov_money BOOLEAN NOT NULL DEFAULT FALSE;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_etablissement_operateur_paiement') THEN
        ALTER TABLE etablissement ADD CONSTRAINT ck_etablissement_operateur_paiement
            CHECK (mode_paiement_service = 'ESPECES' OR paiement_wave OR paiement_orange_money OR paiement_moov_money);
    END IF;
END $$;