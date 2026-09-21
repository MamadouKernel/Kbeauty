-- Le gerant choisit le mode de reglement des prestations de sa boutique.
-- Ce flux est distinct du paiement de l'abonnement de la boutique a Keke Beauty.
ALTER TABLE etablissement
    ADD COLUMN mode_paiement_service VARCHAR(12) NOT NULL DEFAULT 'ESPECES',
    ADD COLUMN paiement_wave BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN paiement_orange_money BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN paiement_moov_money BOOLEAN NOT NULL DEFAULT FALSE,
    ADD CONSTRAINT ck_etablissement_mode_paiement_service
        CHECK (mode_paiement_service IN ('ESPECES', 'EN_LIGNE', 'MIXTE')),
    ADD CONSTRAINT ck_etablissement_operateur_paiement
        CHECK (mode_paiement_service = 'ESPECES' OR paiement_wave OR paiement_orange_money OR paiement_moov_money);

ALTER TABLE rdv
    ADD COLUMN mode_paiement_service VARCHAR(12) NOT NULL DEFAULT 'ESPECES',
    ADD CONSTRAINT ck_rdv_mode_paiement_service
        CHECK (mode_paiement_service IN ('ESPECES', 'EN_LIGNE'));

-- Preserve le mode reel des rendez-vous ayant deja une transaction en ligne.
UPDATE rdv r
SET mode_paiement_service = 'EN_LIGNE'
WHERE EXISTS (SELECT 1 FROM transaction_rdv tr WHERE tr.id_rdv = r.id_rdv);
