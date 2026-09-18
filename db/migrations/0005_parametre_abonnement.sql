-- Migration 0005 : table de configuration parametre_abonnement (feature 008-abonnement-paiement).
-- Tarif standard par periodicite, modifiable par l'administrateur (FR-007) ; n'affecte que les
-- souscriptions futures car le montant est copie sur l'abonnement a la creation.

CREATE TABLE parametre_abonnement (
    periodicite periodicite_enum PRIMARY KEY,
    montant NUMERIC(10,2) NOT NULL CHECK (montant > 0)
);

INSERT INTO parametre_abonnement (periodicite, montant) VALUES
    ('MENSUEL', 5000.00),
    ('ANNUEL', 50000.00);
