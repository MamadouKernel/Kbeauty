-- Un meme compte Google peut etre utilise comme cliente et comme partenaire.
DROP INDEX IF EXISTS ux_utilisateur_google_subject;
CREATE UNIQUE INDEX ux_utilisateur_google_subject_type
    ON utilisateur (google_subject, type_compte)
    WHERE google_subject IS NOT NULL AND date_suppression IS NULL;


CREATE TABLE google_partner_onboarding_token (
    token_hash CHAR(64) PRIMARY KEY,
    id_utilisateur UUID NOT NULL REFERENCES utilisateur(id_utilisateur) ON DELETE CASCADE,
    expire_le TIMESTAMPTZ NOT NULL,
    date_creation TIMESTAMPTZ NOT NULL DEFAULT now()
);
