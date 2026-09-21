CREATE TABLE compte_admin (
    id_admin UUID PRIMARY KEY DEFAULT gen_random_uuid(), nom VARCHAR(120) NOT NULL,
    email VARCHAR(180) NOT NULL UNIQUE, mot_de_passe_hash TEXT NOT NULL,
    role VARCHAR(30) NOT NULL CHECK (role IN ('SUPER_ADMIN','KYC','SUPPORT','COMPTABLE')),
    actif BOOLEAN NOT NULL DEFAULT TRUE, date_creation TIMESTAMPTZ NOT NULL DEFAULT now(),
    derniere_connexion TIMESTAMPTZ
);
CREATE TABLE journal_admin (
    id_journal UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    id_admin UUID REFERENCES compte_admin(id_admin) ON DELETE SET NULL,
    nom_admin VARCHAR(120) NOT NULL, action VARCHAR(80) NOT NULL,
    type_cible VARCHAR(50), id_cible UUID, details TEXT, adresse_ip VARCHAR(64),
    date_action TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_journal_admin_date ON journal_admin(date_action DESC);
CREATE INDEX idx_journal_admin_cible ON journal_admin(type_cible, id_cible);
