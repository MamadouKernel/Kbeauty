-- Migration 0006 : modération back-office (feature 009-moderation-back-office, US-19).
-- Attribut de suspension orthogonal a statut_kyc (etablissement) et au processus d'authentification
-- (utilisateur) : un compte/etablissement suspendu conserve son etat KYC/donnees, il est seulement
-- exclu des fonctionnalites principales (voir spec.md Assumptions).

ALTER TABLE utilisateur ADD COLUMN est_suspendu BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE etablissement ADD COLUMN est_suspendu BOOLEAN NOT NULL DEFAULT FALSE;
