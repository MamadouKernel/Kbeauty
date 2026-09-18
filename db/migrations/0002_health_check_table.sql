-- Migration 0002 : table technique pour la verification de lecture/ecriture reelle (feature 002-scaffold-dotnet).
-- Ne contient jamais de donnees persistantes : l'API insere puis annule (ROLLBACK) dans la meme transaction.

CREATE TABLE health_check (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    checked_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
