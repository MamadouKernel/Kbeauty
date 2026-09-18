# Research: Abonnement et Paiement

## Décision 1 — Agrégateur de paiement
**Décision (mise à jour 2026-09-18)**: `IPaymentGateway` implémentée par `WinPayerGateway` (compte
marchand WiniPayer réel créé, TEST). Échoue explicitement (`Success = false`) si le compte marchand
n'est pas configuré (`Billing:WiniPayer:MerchantApply`/`TestTokenKey`), sans lever d'exception.
**Historique**: initialement prévu avec CinetPay (jamais de compte créé), remplacé par WiniPayer sur
demande explicite de l'utilisateur (compte créé via https://manager.winipayer.com).
**Rationale**: identique au pattern déjà validé pour Zavu (003/004/006/007) pour l'échec explicite —
permet de développer et tester la logique métier sans dépendre d'un service externe disponible.
**Alternatives rejetées**: mock permanent en profondeur (masquerait l'état "non configuré" en prod) ;
lever une exception (romprait le contrat "reflète fidèlement le résultat" du FR-002).

## Décision 4 (mise à jour) — WiniPayer est asynchrone (checkout hébergé), pas un paiement synchrone
**Décision**: `POST /etablissements/{id}/abonnements` crée l'abonnement `IMPAYE` et retourne un
`checkoutUrl` (lien de paiement hébergé WiniPayer) au lieu d'un résultat ACTIF/IMPAYE immédiat. Le
résultat réel arrive via `POST /webhooks/winipayer/callback`, dont la signature (`hash` =
`sha256(privateKey + uuid + crypto + amount + created_at)`) est vérifiée avant toute mise à jour.
**Rationale**: contrairement à l'hypothèse initiale (CinetPay simulé comme synchrone), l'API réelle
de WiniPayer (doc `docs.winipayer.com`) ne fait que générer un lien ; le client paie sur une page
hébergée, et le marchand est notifié après coup. FR-002 ("reflète fidèlement le résultat") est
respecté par cette mise à jour asynchrone plutôt qu'un faux résultat immédiat.
**Alternatives rejetées**: appeler `checkout/standard/detail/:uuid` en boucle juste après la
création pour simuler un résultat synchrone — contraire au fonctionnement réel du paiement (le
client n'a pas encore eu le temps de payer), aurait donné un IMPAYE quasi systématique inutile.
**Impact**: `transaction.canal_paiement` devient nullable (le canal n'est connu qu'après paiement,
choisi par le client sur la page WiniPayer) ; nouvelle colonne `reference_externe` (uuid WiniPayer)
et `operateur_externe` (opérateur réel retourné, ex. `wave-cote-divoire`) — migration 0007.

## Décision 2 — Un seul abonnement ACTIF par établissement
**Décision**: contrainte vérifiée atomiquement au niveau SQL via
`INSERT ... SELECT ... WHERE NOT EXISTS (SELECT 1 FROM abonnement WHERE id_etablissement = @id AND statut = 'ACTIF')`.
**Rationale**: même pattern que le chevauchement de créneaux RDV (007) — élimine les races entre
requêtes concurrentes, contrairement à un check applicatif read-then-write.
**Alternatives rejetées**: contrainte UNIQUE partielle PostgreSQL (`WHERE statut = 'ACTIF'`) — viable
mais moins explicite dans le code métier ; conservée comme option si un bug de concurrence apparaît.

## Décision 3 — Tarif standard par périodicité
**Décision**: nouvelle table de configuration `parametre_abonnement (periodicite, montant)`, lue au
moment de la souscription, modifiable par l'admin, sans effet rétroactif (le montant est copié sur
l'abonnement à la création).
**Rationale**: FR-007 exige une modification sans redéploiement ; `abonnement` stocke déjà le montant
figé par souscription donc ne peut pas aussi porter le tarif "courant".
**Alternatives rejetées**: constante applicative (contraire à FR-007) ; réutiliser `abonnement` comme
tarif de référence (ambigu entre montant historique et montant courant).

## Décision 4 — Relance des impayés
**Décision**: réutilise `IZavuWhatsAppPartnerNotifier` existant (003/006), même pattern "tentative,
échec explicite si non configuré".
**Rationale**: cohérence avec le reste du projet, aucun nouveau canal à intégrer.
**Alternatives rejetées**: email (hors périmètre CDC, aucun compte SMTP prévu).
