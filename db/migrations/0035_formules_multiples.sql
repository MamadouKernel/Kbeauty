-- Passage d'un modele fige a deux formules (FREE/PRO, code en dur) a des formules
-- multiples, entierement creables/modifiables/supprimables par l'admin (US demandee :
-- "permettre a l'admin de creer n packages au lieu de 2"). Chaque formule porte desormais
-- son propre libelle, son etat actif/inactif, un flag de formule par defaut (attribuee aux
-- boutiques sans abonnement actif) et son propre tarif mensuel/annuel.

ALTER TABLE parametre_formule DROP CONSTRAINT parametre_formule_formule_check;
ALTER TABLE parametre_formule ALTER COLUMN formule TYPE VARCHAR(30);
ALTER TABLE parametre_formule ADD COLUMN libelle VARCHAR(60) NOT NULL DEFAULT '';
ALTER TABLE parametre_formule ADD COLUMN est_actif BOOLEAN NOT NULL DEFAULT TRUE;
ALTER TABLE parametre_formule ADD COLUMN est_defaut BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE parametre_formule ADD COLUMN ordre_affichage INTEGER NOT NULL DEFAULT 0;
ALTER TABLE parametre_formule ADD COLUMN tarif_mensuel NUMERIC(10,2) NULL CHECK (tarif_mensuel IS NULL OR tarif_mensuel >= 0);
ALTER TABLE parametre_formule ADD COLUMN tarif_annuel NUMERIC(10,2) NULL CHECK (tarif_annuel IS NULL OR tarif_annuel >= 0);

UPDATE parametre_formule SET libelle = 'Free', est_defaut = TRUE, ordre_affichage = 0 WHERE formule = 'FREE';
UPDATE parametre_formule SET libelle = 'Pro', ordre_affichage = 1,
    tarif_mensuel = (SELECT montant FROM parametre_abonnement WHERE periodicite = 'MENSUEL'),
    tarif_annuel = (SELECT montant FROM parametre_abonnement WHERE periodicite = 'ANNUEL')
WHERE formule = 'PRO';

-- Garantit qu'il existe au plus une formule par defaut a tout instant (l'application
-- interdit de son cote la suppression de la derniere formule ou de la formule par defaut).
CREATE UNIQUE INDEX ux_parametre_formule_un_defaut ON parametre_formule ((est_defaut)) WHERE est_defaut;

-- Chaque abonnement souscrit reference desormais la formule choisie (auparavant implicite :
-- un abonnement ACTIF signifiait toujours PRO, seul pack payant possible).
ALTER TABLE abonnement ADD COLUMN formule VARCHAR(30) NOT NULL DEFAULT 'PRO' REFERENCES parametre_formule(formule);
ALTER TABLE abonnement ALTER COLUMN formule DROP DEFAULT;

-- Le tarif global unique (parametre_abonnement) est remplace par le tarif propre a chaque
-- formule (colonnes ci-dessus) ; sa valeur a deja ete recuperee pour PRO.
DROP TABLE parametre_abonnement;
