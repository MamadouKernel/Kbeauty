-- Operations financieres Wave : remboursements Keke Protect et reversement des pourboires.
ALTER TABLE transaction_rdv
    ADD COLUMN IF NOT EXISTS fournisseur_paiement VARCHAR(20) NOT NULL DEFAULT 'WINIPAYER',
    ADD COLUMN IF NOT EXISTS statut_remboursement VARCHAR(20),
    ADD COLUMN IF NOT EXISTS erreur_remboursement TEXT,
    ADD COLUMN IF NOT EXISTS tentatives_remboursement INTEGER NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS date_tentative_remboursement TIMESTAMPTZ;

ALTER TABLE pourboire
    ADD COLUMN IF NOT EXISTS idempotency_key VARCHAR(255),
    ADD COLUMN IF NOT EXISTS reference_reversement VARCHAR(100),
    ADD COLUMN IF NOT EXISTS statut_reversement VARCHAR(20) NOT NULL DEFAULT 'NON_DEMARRE',
    ADD COLUMN IF NOT EXISTS erreur_reversement TEXT,
    ADD COLUMN IF NOT EXISTS tentatives_reversement INTEGER NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS date_tentative_reversement TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS date_reversement TIMESTAMPTZ;

CREATE UNIQUE INDEX IF NOT EXISTS ux_pourboire_idempotency_key
    ON pourboire(idempotency_key) WHERE idempotency_key IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_pourboire_reversement
    ON pourboire(statut_paiement, statut_reversement);
