-- Migration 0032 : 2FA "par etape" (step-up) pour les comptes CLIENT et PARTENAIRE connectes via
-- Google. Ne s'applique qu'aux connexions depuis un appareil non reconnu (pas de jeton d'appareil
-- de confiance valide) - jamais a chaque connexion, pour ne pas detruire l'interet du "Continuer
-- avec Google" (sans friction). Le code n'est jamais stocke en clair (meme convention que
-- otp_challenge, feature 003).

CREATE TABLE step_up_challenge (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    id_utilisateur UUID NOT NULL REFERENCES utilisateur(id_utilisateur) ON DELETE CASCADE,
    code_hash VARCHAR(64) NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'PENDING',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_step_up_challenge_lookup ON step_up_challenge(id_utilisateur, status);
