# Contracts: Historique des rendez-vous client et paiement en ligne

Toutes les routes client existantes utilisent déjà l'en-tête `X-Client-Id` (GUID) comme mécanisme
d'autorisation (voir 003/007) — inchangé ici.

## `GET /rdv`
Liste l'historique complet du client authentifié (US1).

**Headers**: `X-Client-Id: <guid>` (obligatoire)

**200 OK** — `RdvHistoriqueItem[]`, triés par `dateHeureDebut` décroissant :
```json
[
  {
    "idRdv": "guid",
    "nomEtablissement": "string",
    "libellePrestation": "string",
    "dateHeureDebut": "2026-09-20T14:30:00Z",
    "statutRdv": "CONFIRME",
    "statutPaiement": null
  }
]
```
`statutPaiement` : `null` (aucun paiement en ligne tenté) | `"EN_COURS"` | `"REUSSIE"` | `"ECHOUEE"`.

**401 Unauthorized** — `X-Client-Id` absent ou invalide : `{ "status": "unauthorized" }`.

Liste vide (`[]`) si le client n'a aucun RDV — jamais d'erreur pour ce cas (cf. spec, Acceptance
Scenario US1.2).

---

## `POST /rdv` (étendu)
Comportement existant inchangé (création de RDV) ; ajoute un champ optionnel.

**Body** (champ ajouté en gras) :
```json
{
  "idEtablissement": "guid",
  "idPrestation": "guid",
  "dateHeureDebut": "2026-09-20T14:30:00Z",
  "payerEnLigne": false
}
```
`payerEnLigne` absent ou `false` → comportement actuel inchangé (paiement sur place, réponse identique
à aujourd'hui : `{ "idRdv": "guid", "statut": "DEMANDE" }`).

`payerEnLigne: true` → **201 Created** avec un champ supplémentaire :
```json
{
  "idRdv": "guid",
  "statut": "DEMANDE",
  "paiement": {
    "statutPaiement": "EN_COURS",
    "lienPaiement": "https://checkout.winipayer.com/...",
    "referenceExterne": "uuid-winipayer"
  }
}
```
Si l'agrégateur n'est pas configuré ou refuse la demande (`IPaymentGateway.InitiateAsync` renvoie
`Success=false`, comme pour 008) : le RDV est tout de même créé (le paiement en ligne est un
supplément, pas une condition bloquante — FR-004), et `paiement` vaut :
```json
{ "statutPaiement": "ECHOUEE", "lienPaiement": null, "referenceExterne": null }
```
avec un message applicatif invitant à payer sur place.

---

## `POST /rdv/{id}/paiement/verifier`
Réconciliation manuelle (US3), même principe que
`POST /partenaire/.../abonnements/{id}/paiement/verifier` (008).

**Headers**: `X-Client-Id: <guid>` (le RDV doit appartenir à ce client)

**200 OK**:
```json
{ "statutPaiement": "REUSSIE" }
```
(ou `"EN_COURS"` si toujours en attente côté agrégateur, ou `"ECHOUEE"`)

**404 Not Found** — RDV introuvable, n'appartient pas au client, ou aucune transaction de paiement en
ligne n'existe pour ce RDV.

**503 Service Unavailable** — agrégateur non configuré : `{ "status": "not_configured" }` (même
sémantique que 008).

---

## `POST /webhooks/winipayer/callback` (étendu, URL inchangée)
Comportement existant (008) inchangé pour les références d'abonnement. Ajout : si
`IAbonnementRepository.MarquerPaiementAsync` renvoie `false` (référence inconnue de ce repository), le
contrôleur essaie `IRdvPaiementRepository.MarquerPaiementAsync(uuid, paiementReussi, operateur, ct)`
avec la même signature/vérification de hash (déjà faite une seule fois avant le routage). Réponse
inchangée : `{ "status": "applied" | "already_processed_or_unknown" }`.
