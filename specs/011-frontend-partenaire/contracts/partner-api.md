# API Contract: Endpoints partenaire ajoutés (feature 011, frontend)

## GET /partenaire/etablissements
Liste les établissements gérés par le partenaire authentifié.
**Auth**: header `X-Partner-Id` (guid).

**Responses**:
- `200 OK` — `[{ "idEtablissement", "nomEtablissement", "statutKyc", "estSuspendu" }, ...]`
- `401 Unauthorized` — header absent/malformé

## GET /partenaire/etablissements/{id}/rdv
Liste les RDV de l'établissement `{id}` (tous statuts).
**Auth**: header `X-Partner-Id`, vérifié par `PartnerOwnershipFilter` (existant).

**Responses**:
- `200 OK` — `[{ "idRdv", "statut", "dateHeureDebut", "libellePrestation", "telephoneClient" }, ...]`
- `401 Unauthorized` — header absent/malformé
- `403 Forbidden` — établissement non possédé par ce gérant
