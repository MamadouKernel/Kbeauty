ALTER TABLE utilisateur ADD COLUMN IF NOT EXISTS points_fidelite INTEGER NOT NULL DEFAULT 0 CHECK(points_fidelite>=0);
CREATE TABLE IF NOT EXISTS fidelite_mouvement(
 id_mouvement UUID PRIMARY KEY DEFAULT gen_random_uuid(), id_utilisateur UUID NOT NULL REFERENCES utilisateur(id_utilisateur) ON DELETE CASCADE,
 id_rdv UUID REFERENCES rdv(id_rdv) ON DELETE SET NULL, type_mouvement VARCHAR(40) NOT NULL, points INTEGER NOT NULL,
 libelle VARCHAR(160) NOT NULL, date_creation TIMESTAMPTZ NOT NULL DEFAULT now(), UNIQUE(id_rdv,type_mouvement));
CREATE OR REPLACE FUNCTION keke_award_completed_rdv() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN IF NEW.statut_rdv='TERMINE' AND OLD.statut_rdv IS DISTINCT FROM 'TERMINE' THEN
 INSERT INTO fidelite_mouvement(id_utilisateur,id_rdv,type_mouvement,points,libelle) VALUES(NEW.id_utilisateur_client,NEW.id_rdv,'RDV_TERMINE',50,'Rendez-vous réalisé') ON CONFLICT DO NOTHING;
 IF FOUND THEN UPDATE utilisateur SET points_fidelite=points_fidelite+50 WHERE id_utilisateur=NEW.id_utilisateur_client; END IF;
END IF; RETURN NEW; END $$;
DROP TRIGGER IF EXISTS trg_keke_award_completed_rdv ON rdv;
CREATE TRIGGER trg_keke_award_completed_rdv AFTER UPDATE OF statut_rdv ON rdv FOR EACH ROW EXECUTE FUNCTION keke_award_completed_rdv();
CREATE OR REPLACE FUNCTION keke_award_review() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE client_id UUID; BEGIN SELECT id_utilisateur_client INTO client_id FROM rdv WHERE id_rdv=NEW.id_rdv;
 INSERT INTO fidelite_mouvement(id_utilisateur,id_rdv,type_mouvement,points,libelle) VALUES(client_id,NEW.id_rdv,'AVIS_CERTIFIE',100,'Avis certifié publié') ON CONFLICT DO NOTHING;
 IF FOUND THEN UPDATE utilisateur SET points_fidelite=points_fidelite+100 WHERE id_utilisateur=client_id; END IF; RETURN NEW; END $$;
DROP TRIGGER IF EXISTS trg_keke_award_review ON avis;
CREATE TRIGGER trg_keke_award_review AFTER INSERT ON avis FOR EACH ROW EXECUTE FUNCTION keke_award_review();