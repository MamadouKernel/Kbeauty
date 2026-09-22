-- Migration 0034 : enrichissement OpenStreetMap (recherche d'adresse + adresse lisible).
-- adresse_texte est renseignee une seule fois, au moment de l'inscription (reverse-geocoding
-- Nominatim sur la position choisie par le partenaire), jamais recalculee en boucle a chaque
-- affichage (politique d'usage Nominatim : pas d'appels repetes/bulk sur la meme donnee).

ALTER TABLE etablissement ADD COLUMN adresse_texte VARCHAR(255);
