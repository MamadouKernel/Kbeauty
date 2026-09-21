-- Migration 0011 : complete les 4 extensions "reduites volontairement" a scope plein
-- (voir specs/018-extensions-completes/spec.md).
-- Chaque ALTER TYPE ADD VALUE est suivi de son usage dans le meme fichier : psql applique
-- ce script statement par statement en autocommit (pas de BEGIN englobant), donc la nouvelle
-- valeur d'enum est bien visible aux statements suivants du meme fichier.

-- Parcours 5 : compte de connexion collaboratrice (reutilise le mecanisme OTP generique existant,
-- deja parametre par TypeCompte depuis la feature 006).
ALTER TYPE type_compte_enum ADD VALUE IF NOT EXISTS 'COLLABORATEUR';

ALTER TABLE collaborateur ADD COLUMN telephone VARCHAR(20) UNIQUE;
ALTER TABLE collaborateur ADD COLUMN id_utilisateur UUID REFERENCES utilisateur(id_utilisateur) ON DELETE SET NULL;

-- Planning individuel : un RDV peut etre assigne a une collaboratrice precise.
ALTER TABLE rdv ADD COLUMN id_collaborateur UUID REFERENCES collaborateur(id_collaborateur) ON DELETE SET NULL;
CREATE INDEX idx_rdv_collaborateur ON rdv(id_collaborateur);

-- Fiches techniques clientes (une fiche par RDV, redigee par la collaboratrice assignee).
CREATE TABLE fiche_technique (
    id_fiche UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    id_rdv UUID NOT NULL UNIQUE REFERENCES rdv(id_rdv) ON DELETE CASCADE,
    id_collaborateur UUID NOT NULL REFERENCES collaborateur(id_collaborateur) ON DELETE CASCADE,
    notes TEXT NOT NULL,
    date_creation TIMESTAMPTZ NOT NULL DEFAULT now(),
    date_modification TIMESTAMPTZ
);

-- Parcours 3 : pourboire direct a la praticienne, 0% commission plateforme (voir spec pour le
-- constat honnete sur le transfert reel des fonds : WinPayer est un encaisseur marchand pour
-- Keke Beauty, pas une plateforme de paiement P2P vers un tiers individuel).
CREATE TABLE pourboire (
    id_pourboire UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    id_rdv UUID NOT NULL REFERENCES rdv(id_rdv) ON DELETE CASCADE,
    id_collaborateur UUID NOT NULL REFERENCES collaborateur(id_collaborateur) ON DELETE CASCADE,
    montant NUMERIC(10,2) NOT NULL CHECK (montant > 0),
    statut_paiement VARCHAR(20) NOT NULL DEFAULT 'EN_ATTENTE',
    reference_externe VARCHAR(100),
    date_creation TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_pourboire_collaborateur ON pourboire(id_collaborateur);
CREATE INDEX idx_pourboire_rdv ON pourboire(id_rdv);

-- Parcours 6 : litiges (client ou partenaire peut declarer un litige sur un RDV ; l'admin
-- instruit et resout). "Acomptes" et "conciergerie" restent hors perimetre (voir spec, Assumptions) :
-- aucun besoin metier concret identifie au-dela du paiement en ligne deja existant (feature 013).
CREATE TABLE litige (
    id_litige UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    id_rdv UUID NOT NULL REFERENCES rdv(id_rdv) ON DELETE CASCADE,
    id_utilisateur_declarant UUID NOT NULL REFERENCES utilisateur(id_utilisateur) ON DELETE CASCADE,
    motif TEXT NOT NULL,
    statut_litige VARCHAR(20) NOT NULL DEFAULT 'OUVERT',
    resolution TEXT,
    date_creation TIMESTAMPTZ NOT NULL DEFAULT now(),
    date_resolution TIMESTAMPTZ
);
CREATE INDEX idx_litige_statut ON litige(statut_litige);
CREATE INDEX idx_litige_rdv ON litige(id_rdv);
