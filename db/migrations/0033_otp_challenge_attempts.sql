-- Migration 0033 : correctif audit securite (brute force OTP, feature 003/006).
-- Le rate limiting existant sur /auth/otp/* est uniquement par adresse IP (Program.cs, policy
-- "auth" : 10 req/5min) : un attaquant disposant de plusieurs IP peut distribuer ses tentatives
-- de deviner le code a 6 chiffres sur un meme numero de telephone cible sans jamais etre bloque.
-- On ajoute un compteur d'essais par challenge, invalide independamment de l'IP source.

ALTER TABLE otp_challenge ADD COLUMN attempts SMALLINT NOT NULL DEFAULT 0;
