-- Authentification par email + mot de passe, additive aux flux OTP telephone et Google existants.
-- password_hash reste NULL pour tout compte cree via OTP/Google (aucune donnee existante affectee).
ALTER TABLE utilisateur ADD COLUMN IF NOT EXISTS password_hash TEXT;
ALTER TABLE utilisateur ADD COLUMN IF NOT EXISTS email_verifie BOOLEAN NOT NULL DEFAULT FALSE;

-- Un email ne peut servir qu'a un seul compte par mot de passe et par type de compte (evite les
-- doublons d'inscription). Les comptes OTP/Google (password_hash NULL) ne sont pas contraints ici.
CREATE UNIQUE INDEX IF NOT EXISTS ux_utilisateur_email_password
    ON utilisateur (lower(email), type_compte)
    WHERE password_hash IS NOT NULL AND date_suppression IS NULL;

-- Codes a 6 chiffres a usage unique pour la verification d'email et la reinitialisation de mot
-- de passe. Cle primaire (id_utilisateur, purpose) et non code_hash : un code a 6 chiffres n'a que
-- 1 000 000 de valeurs possibles, donc deux utilisateurs pourraient recevoir le meme code au meme
-- moment - code_hash ne peut pas etre une cle primaire sans risquer un conflit d'insertion.
CREATE TABLE IF NOT EXISTS password_auth_token (
    id_utilisateur UUID NOT NULL REFERENCES utilisateur(id_utilisateur) ON DELETE CASCADE,
    purpose VARCHAR(20) NOT NULL CHECK (purpose IN ('VERIFY_EMAIL', 'RESET_PASSWORD')),
    code_hash TEXT NOT NULL,
    expire_le TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (id_utilisateur, purpose)
);
