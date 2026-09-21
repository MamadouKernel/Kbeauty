ALTER TABLE rdv ADD COLUMN IF NOT EXISTS retard_minutes SMALLINT CHECK (retard_minutes IN (10,15,30));
ALTER TABLE rdv ADD COLUMN IF NOT EXISTS date_signalement_retard TIMESTAMPTZ;
ALTER TABLE transaction_rdv ADD COLUMN IF NOT EXISTS eligible_keke_protect BOOLEAN;