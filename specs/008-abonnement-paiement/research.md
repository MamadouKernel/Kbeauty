# Research: Abonnement et Paiement

## Décision 1 — Agrégateur de paiement
**Décision**: `IPaymentGateway` avec une implémentation `CinetPayGateway` qui échoue explicitement
(`success = false`) si `Billing:CinetPay:ApiKey` est absent de la config, sans lever d'exception.
**Rationale**: identique au pattern déjà validé pour Zavu (003/004/006/007) — permet de développer et
tester la logique métier (abonnement/transaction) sans compte tiers réel.
**Alternatives rejetées**: mock permanent en profondeur (masquerait l'état "non configuré" en prod) ;
lever une exception (romprait le contrat "reflète fidèlement le résultat" du FR-002).

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
