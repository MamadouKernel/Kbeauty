# Phase 1 — Data Model: Squelette Applicatif Backend

Cette feature n'introduit aucune entité métier. Elle ajoute une seule table technique, nécessaire à
la vérification d'écriture réelle exigée par FR-003.

## Entité technique ajoutée

### health_check

| Colonne | Type | Règle |
|---|---|---|
| id | UUID PK | `gen_random_uuid()` |
| checked_at | TIMESTAMPTZ | Horodatage de la tentative d'écriture, défaut `now()` |

Aucune ligne n'est censée persister durablement : chaque appel à `/health/db` insère puis annule
(`ROLLBACK`) sa propre ligne dans la même transaction (voir `research.md`, Décision 3). La table existe
uniquement pour offrir une cible d'écriture qui n'appartient à aucune entité métier.

## Entités métier (rappel, non modifiées ici)

Voir `docs/merise/04-mld.md` pour le détail complet. Les entités Domain créées dans cette feature
(`src/KekeBeauty.Domain/Entities/`) sont des classes C# vides de logique, une par table du MLD, prêtes
à être enrichies par les futures features métier (US-03 et suivantes du Product Backlog) : `Utilisateur`,
`Etablissement`, `Categorie`, `Pays`/`Region`/`Ville`/`Commune`, `Media`, `Prestation`, `Rdv`,
`Abonnement`, `Transaction`.

## État et transitions

Aucune machine à états métier n'est concernée par cette feature. La seule "transition" est celle de la
transaction de vérification d'écriture : ouverte → ligne insérée → annulée (jamais commitée).
