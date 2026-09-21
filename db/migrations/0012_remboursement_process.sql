-- Migration 0012 : met en place le PROCESSUS de remboursement (file d'attente + traitement trace
-- par l'admin), sans integration d'API de virement (WiniPayer n'en expose pas - voir
-- specs/018-extensions-completes/spec.md). Avant cette migration, AnnulerRdvUseCase marquait
-- directement REMBOURSEE sans aucune trace d'un traitement reel ; desormais l'annulation cree une
-- DEMANDE explicite, que l'admin doit traiter manuellement (avec reference de virement saisie).

ALTER TYPE statut_transaction_enum ADD VALUE IF NOT EXISTS 'REMBOURSEMENT_DEMANDE';

ALTER TABLE transaction_rdv ADD COLUMN reference_remboursement VARCHAR(100);
ALTER TABLE transaction_rdv ADD COLUMN date_remboursement TIMESTAMPTZ;

CREATE INDEX idx_transaction_rdv_statut ON transaction_rdv(statut_transaction);
