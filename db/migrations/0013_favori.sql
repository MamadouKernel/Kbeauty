-- Migration 0013 : favoris client (Parcours 1) - le coeur bouton "favoris" de la fiche
-- etablissement n'etait jusqu'ici que du toggle visuel local sans aucune persistence, ce qui
-- viole le principe "jamais de fausse fonctionnalite" du projet. Mise en place minimale : une
-- table de liaison, aucune donnee fabriquee.

CREATE TABLE favori (
    id_utilisateur_client UUID NOT NULL REFERENCES utilisateur(id_utilisateur) ON DELETE CASCADE,
    id_etablissement UUID NOT NULL REFERENCES etablissement(id_etablissement) ON DELETE CASCADE,
    date_creation TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (id_utilisateur_client, id_etablissement)
);

CREATE INDEX idx_favori_etablissement ON favori(id_etablissement);
