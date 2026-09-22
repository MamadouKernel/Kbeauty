-- Contenu libre (avantages marketing) et promotions temporaires pour les packs d'abonnement.
-- Complete 0035 (formules multiples) : l'admin peut desormais ajouter des lignes de texte libre
-- par pack (non appliquees techniquement, purement descriptives) et definir une reduction en
-- pourcentage avec date de fin optionnelle (indefinie si absente, jusqu'a suppression manuelle).

ALTER TABLE parametre_formule ADD COLUMN promo_pourcentage SMALLINT NULL CHECK (promo_pourcentage IS NULL OR (promo_pourcentage BETWEEN 1 AND 95));
ALTER TABLE parametre_formule ADD COLUMN promo_fin TIMESTAMPTZ NULL;

CREATE TABLE parametre_formule_avantage (
    id_avantage UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    formule VARCHAR(30) NOT NULL REFERENCES parametre_formule(formule) ON DELETE CASCADE,
    libelle VARCHAR(140) NOT NULL,
    ordre_affichage INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX idx_formule_avantage_formule ON parametre_formule_avantage(formule);
