-- Migration 0004 : seed geographique minimal (feature 004-onboarding-partenaire).
-- Dependance decouverte : etablissement.id_commune est NOT NULL mais aucune donnee
-- geographique n'existe encore (US-10, back-office referentiels, pas encore implemente).
-- Ce seed n'est PAS une gestion CRUD du referentiel : juste le minimum pour avancer.

INSERT INTO pays (id_pays, libelle_pays)
VALUES ('00000000-0000-0000-0000-000000000001', 'Côte d''Ivoire');

INSERT INTO region (id_region, libelle_region, id_pays)
VALUES ('00000000-0000-0000-0000-000000000002', 'Abidjan', '00000000-0000-0000-0000-000000000001');

INSERT INTO ville (id_ville, libelle_ville, id_region)
VALUES ('00000000-0000-0000-0000-000000000003', 'Abidjan', '00000000-0000-0000-0000-000000000002');

INSERT INTO commune (id_commune, libelle_commune, id_ville)
VALUES ('00000000-0000-0000-0000-000000000004', 'Cocody', '00000000-0000-0000-0000-000000000003');
