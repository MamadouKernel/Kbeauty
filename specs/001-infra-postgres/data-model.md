# Phase 1 — Data Model: Infrastructure Base de Données Reproductible

Cette feature n'introduit pas de nouvelle entité métier : elle rend exécutable et évolutif le modèle
déjà validé en MERISE. Référence complète : [docs/merise/03-mcd.md](../../docs/merise/03-mcd.md),
[04-mld.md](../../docs/merise/04-mld.md), [05-mpd.sql](../../docs/merise/05-mpd.sql).

## Entité technique ajoutée par cette feature

### schema_migrations

Table de contrôle interne (pas une entité métier) permettant de savoir quelles migrations ont déjà
été appliquées à un environnement donné.

| Colonne | Type | Règle |
|---|---|---|
| version | VARCHAR(20) PK | Numéro de migration, ex. `0001` |
| description | TEXT | Nom du fichier (sans le numéro) |
| checksum | VARCHAR(64) | SHA-256 du contenu du fichier au moment de l'application |
| applied_at | TIMESTAMPTZ | Horodatage d'application, défaut `now()` |

Règles associées :
- Une migration ne MUST jamais être ré-appliquée si sa `version` est déjà présente dans la table.
- Si le `checksum` stocké diffère du checksum recalculé du fichier correspondant sur disque, le script
  `migrate.sh` MUST échouer (FR-007 — détecte une modification rétroactive non autorisée).

## Entités métier (rappel, non modifiées ici)

Voir MLD (`04-mld.md`) pour le détail complet : `pays`, `region`, `ville`, `commune`, `categorie`,
`utilisateur`, `etablissement`, `etablissement_categorie`, `media`, `prestation`, `rdv`, `abonnement`,
`transaction`. Toutes sont créées par la migration `0001_init_schema.sql` (copie du MPD validé).

## État et transitions

Aucune machine à états métier n'est concernée par cette feature d'infrastructure. Le seul état géré
est celui de `schema_migrations` : chaque ligne passe de "absente" à "appliquée" de façon irréversible
(une nouvelle évolution corrective est une nouvelle migration, jamais une modification d'une ligne
existante).
