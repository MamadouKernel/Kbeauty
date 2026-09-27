-- Parametres issus des essais terrain du 26 septembre 2026.
CREATE TABLE IF NOT EXISTS parametre_plateforme (
    cle VARCHAR(100) PRIMARY KEY,
    valeur TEXT NOT NULL,
    description TEXT,
    date_modification TIMESTAMPTZ NOT NULL DEFAULT now()
);

INSERT INTO parametre_plateforme(cle, valeur, description)
VALUES ('reservation.delai_minimum_minutes', '120', 'Délai minimum avant le début d’un rendez-vous client')
ON CONFLICT (cle) DO NOTHING;

ALTER TABLE etablissement
    ADD COLUMN IF NOT EXISTS consentement_rgpd_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS version_rgpd VARCHAR(20),
    ADD COLUMN IF NOT EXISTS source_creation VARCHAR(30) NOT NULL DEFAULT 'AUTONOME',
    ADD COLUMN IF NOT EXISTS cree_par_admin UUID REFERENCES compte_admin(id_admin) ON DELETE SET NULL;

