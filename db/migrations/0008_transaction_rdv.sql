-- Migration 0008 : paiement en ligne optionnel a la demande de RDV (feature 013).
-- Table dediee (pas de reutilisation de `transaction`, qui porte une FK NOT NULL vers `abonnement` -
-- voir specs/013-historique-rdv-paiement/research.md Decision 1).

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
