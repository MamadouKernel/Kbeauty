-- Profil client, consentements et suppression logique/anonymisation.
ALTER TABLE utilisateur ADD COLUMN IF NOT EXISTS email VARCHAR(254);
ALTER TABLE utilisateur ADD COLUMN IF NOT EXISTS notifications_rdv BOOLEAN NOT NULL DEFAULT TRUE;
ALTER TABLE utilisateur ADD COLUMN IF NOT EXISTS notifications_marketing BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE utilisateur ADD COLUMN IF NOT EXISTS consentement_donnees BOOLEAN NOT NULL DEFAULT TRUE;
ALTER TABLE utilisateur ADD COLUMN IF NOT EXISTS date_suppression TIMESTAMPTZ;
CREATE UNIQUE INDEX IF NOT EXISTS ux_utilisateur_email_type ON utilisateur (lower(email), type_compte) WHERE email IS NOT NULL AND date_suppression IS NULL;
