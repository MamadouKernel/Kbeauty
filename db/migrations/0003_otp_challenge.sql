-- Migration 0003 : table technique otp_challenge (feature 003-auth-client).
-- Le code OTP n'est jamais stocke en clair (FR-010) : seul un hash SHA-256 est conserve.

CREATE TYPE otp_status_enum AS ENUM ('PENDING', 'CONSUMED', 'INVALIDATED');

CREATE TABLE otp_challenge (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    telephone VARCHAR(20) NOT NULL,
    type_compte type_compte_enum NOT NULL,
    code_hash VARCHAR(64) NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    status otp_status_enum NOT NULL DEFAULT 'PENDING',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_otp_challenge_lookup ON otp_challenge(telephone, type_compte, status);
