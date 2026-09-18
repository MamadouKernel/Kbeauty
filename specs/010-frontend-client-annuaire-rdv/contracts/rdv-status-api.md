# API Contract: Statut d'un RDV (ajout minimal pour le frontend)

## GET /rdv/{id}
Retourne le statut courant d'un RDV, pour que le client puisse le rafraîchir après création
(US3 AC3). Aucune modification de l'existant `POST /rdv` / `GET /etablissements/{id}/creneaux`.

**Auth**: header `X-Client-Id` (guid) — doit correspondre au `id_utilisateur_client` du RDV.

**Responses**:
- `200 OK` — `{ "idRdv", "statut": "DEMANDE|CONFIRME|REFUSE", "dateHeureDebut" }`
- `401 Unauthorized` — header absent/malformé
- `403 Forbidden` — RDV n'appartenant pas au client authentifié
- `404 Not Found` — RDV introuvable
