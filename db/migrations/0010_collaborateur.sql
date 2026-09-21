-- Migration 0010 : equipe (collaboratrices) referencee par le gerant partenaire (feature 016).
-- Perimetre volontairement reduit : pas de compte de connexion pour la collaboratrice (voir
-- specs/016-gestion-equipe/spec.md, Assumptions) - simple annuaire cote gerant.

CREATE TABLE collaborateur (
    id_collaborateur UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    nom VARCHAR(100) NOT NULL,
    specialite VARCHAR(150),
    id_etablissement UUID NOT NULL REFERENCES etablissement(id_etablissement) ON DELETE CASCADE,
    date_creation TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_collaborateur_etablissement ON collaborateur(id_etablissement);
