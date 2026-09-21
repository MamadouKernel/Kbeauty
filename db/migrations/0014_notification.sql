-- Migration 0014 : notifications in-app (US-14, Parcours 1). Le canal SMS/WhatsApp existe deja
-- (IRdvNotifier -> Zavu, bloque tant que le compte n'est pas configure) mais le canal "in-app" du
-- US-14 n'etait qu'une icone cloche purement decorative (aucune donnee, aucun clic) sur Home.razor
-- et Carte.razor. Ferme ce gap independamment de Zavu.

CREATE TABLE notification (
    id_notification UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    id_utilisateur UUID NOT NULL REFERENCES utilisateur(id_utilisateur) ON DELETE CASCADE,
    titre VARCHAR(150) NOT NULL,
    message TEXT NOT NULL,
    id_rdv UUID REFERENCES rdv(id_rdv) ON DELETE CASCADE,
    lu BOOLEAN NOT NULL DEFAULT false,
    date_creation TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_notification_utilisateur ON notification(id_utilisateur, lu);
