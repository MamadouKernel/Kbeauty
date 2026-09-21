-- Contenu administrable des packs boutique FREE et PRO.
CREATE TABLE parametre_formule (
    formule VARCHAR(10) PRIMARY KEY CHECK (formule IN ('FREE', 'PRO')),
    limite_prestations INTEGER NULL CHECK (limite_prestations IS NULL OR limite_prestations >= 0),
    limite_rdv_mensuels INTEGER NULL CHECK (limite_rdv_mensuels IS NULL OR limite_rdv_mensuels >= 0),
    paiement_mobile BOOLEAN NOT NULL,
    gestion_equipe BOOLEAN NOT NULL,
    statistiques_avancees BOOLEAN NOT NULL,
    date_modification TIMESTAMPTZ NOT NULL DEFAULT now()
);

INSERT INTO parametre_formule
    (formule, limite_prestations, limite_rdv_mensuels, paiement_mobile, gestion_equipe, statistiques_avancees)
VALUES
    ('FREE', 5, 20, FALSE, FALSE, FALSE),
    ('PRO', NULL, NULL, TRUE, TRUE, TRUE);
