# MLD — Modèle Logique des Données

Notation : `TABLE(colonne, ..., #cle_etrangere)`. Clé primaire soulignée par convention `**pk**`.

```
PAYS(**id_pays**, libelle_pays)

REGION(**id_region**, libelle_region, #id_pays)

VILLE(**id_ville**, libelle_ville, #id_region)

COMMUNE(**id_commune**, libelle_commune, #id_ville)

CATEGORIE(**id_categorie**, libelle_categorie)

UTILISATEUR(**id_utilisateur**, telephone, nom, type_compte, date_creation)

ETABLISSEMENT(**id_etablissement**, nom_etablissement, description, gps_latitude, gps_longitude,
    horaires, numero_service_client, statut_kyc, url_piece_identite, url_photo_devanture,
    #id_utilisateur_gerant, #id_commune)

ETABLISSEMENT_CATEGORIE(**#id_etablissement**, **#id_categorie**)
    -- table d'association N,N entre ETABLISSEMENT et CATEGORIE

MEDIA(**id_media**, type_media, url, ordre_affichage, #id_etablissement)

PRESTATION(**id_prestation**, libelle_prestation, tarif, duree_minutes, #id_etablissement)

RDV(**id_rdv**, date_heure_debut, statut_rdv, date_creation,
    #id_utilisateur_client, #id_etablissement, #id_prestation)

ABONNEMENT(**id_abonnement**, periodicite, date_debut_engagement, montant, statut_abonnement,
    #id_etablissement)

TRANSACTION(**id_transaction**, canal_paiement, statut_transaction, date_transaction, montant,
    #id_abonnement)
```

## Contraintes d'intégrité référentielle
- `ETABLISSEMENT.id_utilisateur_gerant` → `UTILISATEUR.id_utilisateur` (ON DELETE RESTRICT)
- `ETABLISSEMENT.id_commune` → `COMMUNE.id_commune` (ON DELETE RESTRICT)
- `RDV.id_utilisateur_client` → `UTILISATEUR.id_utilisateur` (ON DELETE RESTRICT)
- `RDV.id_etablissement` → `ETABLISSEMENT.id_etablissement` (ON DELETE CASCADE)
- `TRANSACTION.id_abonnement` → `ABONNEMENT.id_abonnement` (ON DELETE RESTRICT)
- `ETABLISSEMENT_CATEGORIE` : clé primaire composite (id_etablissement, id_categorie)
