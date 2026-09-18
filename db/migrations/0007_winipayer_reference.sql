-- Migration 0007 : reference externe WiniPayer sur transaction (feature 008, remplacement CinetPay).
-- Permet de reconcilier le callback WiniPayer (POST /webhooks/winipayer/callback) avec la
-- transaction correspondante via l'uuid de facture WiniPayer.

ALTER TABLE transaction ADD COLUMN reference_externe VARCHAR(64);

CREATE INDEX idx_transaction_reference_externe ON transaction(reference_externe);

-- WiniPayer (checkout hebergee) ne demande pas de canal a l'avance : le client le choisit sur la
-- page de paiement, et l'operateur retourne (ex. "wave-cote-divoire") ne correspond pas a
-- canal_paiement_enum. canal_paiement reste donc NULL pour ces transactions ; l'operateur brut
-- est conserve tel que renvoye par WiniPayer dans operateur_externe.
ALTER TABLE transaction ALTER COLUMN canal_paiement DROP NOT NULL;
ALTER TABLE transaction ADD COLUMN operateur_externe VARCHAR(50);
