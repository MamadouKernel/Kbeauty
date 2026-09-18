# Phase 1 — Data Model: Recherche et Consultation de l'Annuaire par le Client

Cette feature ne crée aucune table. Elle lit les entités déjà modélisées et écrit uniquement via le
mécanisme minimal FR-008 (association catégorie, ajout prestation), déjà couverts par le MPD existant.

## Entités lues

- **etablissement** : `statut_kyc`, `nom_etablissement`, `description`, `gps_latitude/longitude`,
  `numero_service_client`, `id_commune`.
- **etablissement_categorie** : table d'association, utilisée pour le filtre et pour FR-002 (INNER JOIN).
- **categorie** : `libelle_categorie` (lu pour l'affichage, écrit via find-or-create pour FR-008).
- **media** : `type_media`, `url`, `ordre_affichage`, liés à un établissement.
- **prestation** : `libelle_prestation`, `tarif`, `duree_minutes`, liés à un établissement (écrit via FR-008).
- **commune** : `libelle_commune`, utilisée comme filtre de recherche.

## Projections (classes matérialisées par Dapper, cf. convention 003/004)

### EtablissementSummary (résultat de recherche)
`IdEtablissement`, `NomEtablissement`, `LibelleCommune`, `Categories` (liste de libellés).

### EtablissementDetail (fiche complète)
`IdEtablissement`, `NomEtablissement`, `Description`, `NumeroServiceClient`, `LienItineraire`,
`Medias` (liste `{TypeMedia, Url, OrdreAffichage}`), `Prestations` (liste `{Libelle, Tarif, DureeMinutes}`).

## Règles de gestion appliquées

- FR-002 : un établissement sans ligne `etablissement_categorie` n'apparaît jamais en recherche
  (garanti par l'`INNER JOIN`, voir `research.md` Décision 1).
- FR-007 : `EtablissementDetail` est `null` pour tout établissement non `VALIDE`, qu'il existe ou non.
