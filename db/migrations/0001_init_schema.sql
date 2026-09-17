-- Migration 0001 : schema initial complet, copie exacte du MPD MERISE valide
-- (docs/merise/05-mpd.sql). L'extension pgcrypto est deja creee par db/init/000_bootstrap.sql.

CREATE TYPE type_compte_enum AS ENUM ('CLIENT', 'PARTENAIRE', 'ADMIN');
CREATE TYPE statut_kyc_enum AS ENUM ('EN_ATTENTE', 'VALIDE', 'REJETE');
CREATE TYPE type_media_enum AS ENUM ('PHOTO', 'VIDEO');
CREATE TYPE statut_rdv_enum AS ENUM ('DEMANDE', 'CONFIRME', 'REFUSE', 'ANNULE', 'TERMINE');
CREATE TYPE periodicite_enum AS ENUM ('MENSUEL', 'ANNUEL');
CREATE TYPE statut_abonnement_enum AS ENUM ('ACTIF', 'IMPAYE', 'RESILIE');
CREATE TYPE canal_paiement_enum AS ENUM ('WAVE', 'ORANGE_MONEY', 'MTN', 'MOOV', 'VISA', 'MASTERCARD');
CREATE TYPE statut_transaction_enum AS ENUM ('EN_COURS', 'REUSSIE', 'ECHOUEE', 'REMBOURSEE');

CREATE TABLE pays (
    id_pays UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    libelle_pays VARCHAR(100) NOT NULL
);

CREATE TABLE region (
    id_region UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    libelle_region VARCHAR(100) NOT NULL,
    id_pays UUID NOT NULL REFERENCES pays(id_pays) ON DELETE RESTRICT
);

CREATE TABLE ville (
    id_ville UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    libelle_ville VARCHAR(100) NOT NULL,
    id_region UUID NOT NULL REFERENCES region(id_region) ON DELETE RESTRICT
);

CREATE TABLE commune (
    id_commune UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    libelle_commune VARCHAR(100) NOT NULL,
    id_ville UUID NOT NULL REFERENCES ville(id_ville) ON DELETE RESTRICT
);

CREATE TABLE categorie (
    id_categorie UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    libelle_categorie VARCHAR(50) NOT NULL UNIQUE
);

CREATE TABLE utilisateur (
    id_utilisateur UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    telephone VARCHAR(20) NOT NULL,
    nom VARCHAR(100) NOT NULL,
    type_compte type_compte_enum NOT NULL,
    date_creation TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (telephone, type_compte)
);

CREATE TABLE etablissement (
    id_etablissement UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    nom_etablissement VARCHAR(150) NOT NULL,
    description TEXT,
    gps_latitude NUMERIC(9,6) NOT NULL CHECK (gps_latitude BETWEEN -90 AND 90),
    gps_longitude NUMERIC(9,6) NOT NULL CHECK (gps_longitude BETWEEN -180 AND 180),
    horaires JSONB,
    numero_service_client VARCHAR(20) NOT NULL,
    statut_kyc statut_kyc_enum NOT NULL DEFAULT 'EN_ATTENTE',
    url_piece_identite TEXT,
    url_photo_devanture TEXT,
    id_utilisateur_gerant UUID NOT NULL REFERENCES utilisateur(id_utilisateur) ON DELETE RESTRICT,
    id_commune UUID NOT NULL REFERENCES commune(id_commune) ON DELETE RESTRICT,
    date_creation TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE etablissement_categorie (
    id_etablissement UUID NOT NULL REFERENCES etablissement(id_etablissement) ON DELETE CASCADE,
    id_categorie UUID NOT NULL REFERENCES categorie(id_categorie) ON DELETE RESTRICT,
    PRIMARY KEY (id_etablissement, id_categorie)
);

CREATE TABLE media (
    id_media UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    type_media type_media_enum NOT NULL,
    url TEXT NOT NULL,
    ordre_affichage SMALLINT NOT NULL DEFAULT 0,
    id_etablissement UUID NOT NULL REFERENCES etablissement(id_etablissement) ON DELETE CASCADE
);

CREATE TABLE prestation (
    id_prestation UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    libelle_prestation VARCHAR(150) NOT NULL,
    tarif NUMERIC(10,2) NOT NULL CHECK (tarif > 0),
    duree_minutes SMALLINT NOT NULL CHECK (duree_minutes > 0),
    id_etablissement UUID NOT NULL REFERENCES etablissement(id_etablissement) ON DELETE CASCADE
);

CREATE TABLE rdv (
    id_rdv UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    date_heure_debut TIMESTAMPTZ NOT NULL,
    statut_rdv statut_rdv_enum NOT NULL DEFAULT 'DEMANDE',
    date_creation TIMESTAMPTZ NOT NULL DEFAULT now(),
    id_utilisateur_client UUID NOT NULL REFERENCES utilisateur(id_utilisateur) ON DELETE RESTRICT,
    id_etablissement UUID NOT NULL REFERENCES etablissement(id_etablissement) ON DELETE CASCADE,
    id_prestation UUID NOT NULL REFERENCES prestation(id_prestation) ON DELETE RESTRICT
);

CREATE TABLE abonnement (
    id_abonnement UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    periodicite periodicite_enum NOT NULL,
    date_debut_engagement DATE NOT NULL,
    montant NUMERIC(10,2) NOT NULL CHECK (montant > 0),
    statut_abonnement statut_abonnement_enum NOT NULL DEFAULT 'ACTIF',
    id_etablissement UUID NOT NULL REFERENCES etablissement(id_etablissement) ON DELETE RESTRICT
);

CREATE TABLE transaction (
    id_transaction UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    canal_paiement canal_paiement_enum NOT NULL,
    statut_transaction statut_transaction_enum NOT NULL DEFAULT 'EN_COURS',
    date_transaction TIMESTAMPTZ NOT NULL DEFAULT now(),
    montant NUMERIC(10,2) NOT NULL CHECK (montant > 0),
    id_abonnement UUID NOT NULL REFERENCES abonnement(id_abonnement) ON DELETE RESTRICT
);

CREATE INDEX idx_etablissement_commune ON etablissement(id_commune);
CREATE INDEX idx_etablissement_kyc ON etablissement(statut_kyc);
CREATE INDEX idx_rdv_etablissement_date ON rdv(id_etablissement, date_heure_debut);
CREATE INDEX idx_rdv_client ON rdv(id_utilisateur_client);
CREATE INDEX idx_abonnement_etablissement ON abonnement(id_etablissement);
