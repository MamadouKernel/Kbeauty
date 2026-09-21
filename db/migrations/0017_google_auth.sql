-- Connexion Google pour les comptes clients. L'identifiant stable Google (sub), et non l'email,
-- constitue la cle de liaison avec le fournisseur d'identite.
ALTER TABLE utilisateur ALTER COLUMN telephone DROP NOT NULL;
ALTER TABLE utilisateur ADD COLUMN IF NOT EXISTS google_subject VARCHAR(255);
CREATE UNIQUE INDEX IF NOT EXISTS ux_utilisateur_google_subject
    ON utilisateur (google_subject) WHERE google_subject IS NOT NULL AND date_suppression IS NULL;
