-- Migration 0015 : abonnements push web (Parcours 3, "meme le PWA"). Necessaire pour un vrai gap
-- decouvert en construisant les rappels avis/pourboire : rien dans le systeme ne faisait jamais
-- passer un RDV au statut TERMINE (ni job planifie, ni action manuelle), ce qui rendait avis et
-- pourboire inaccessibles en usage reel. Cette migration porte la partie "notification push"
-- (voir aussi RdvCompletionBackgroundService cote API pour le passage automatique a TERMINE).

CREATE TABLE push_subscription (
    id_push_subscription UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    id_utilisateur UUID NOT NULL REFERENCES utilisateur(id_utilisateur) ON DELETE CASCADE,
    endpoint TEXT NOT NULL UNIQUE,
    p256dh VARCHAR(255) NOT NULL,
    auth VARCHAR(255) NOT NULL,
    date_creation TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_push_subscription_utilisateur ON push_subscription(id_utilisateur);
