# Phase 1 — Data Model: Inscription et Validation KYC des Établissements Partenaires

Cette feature réutilise les entités `etablissement` et `utilisateur` déjà modélisées en MERISE, sans
modifier leur structure. Elle introduit uniquement des données de référence géographiques minimales.

## Entité métier réutilisée : etablissement (rappel)

Colonnes exploitées par cette feature (déjà en base, `docs/merise/05-mpd.sql`) :
`nom_etablissement`, `gps_latitude`, `gps_longitude`, `numero_service_client`, `horaires`,
`statut_kyc` (`EN_ATTENTE`/`VALIDE`/`REJETE`), `url_photo_devanture`, `url_piece_identite`,
`id_utilisateur_gerant`, `id_commune`.

## État et transitions (statut_kyc)

```text
(soumission)  → EN_ATTENTE               (FR-004)
EN_ATTENTE    → VALIDE     (admin, FR-007, si les 2 pieces sont presentes)
EN_ATTENTE    → REJETE     (admin, FR-008)
VALIDE        → REJETE     (admin, FR-008 — re-bascule possible, Edge Case)
```

Aucune transition ne ramène `REJETE` vers `EN_ATTENTE` ou `VALIDE` automatiquement — une nouvelle
soumission par le gérant (hors périmètre de cette itération, cf. Edge Case "second dossier") serait
nécessaire pour rouvrir un dossier rejeté.

## Données de référence ajoutées (dépendance découverte, cf. `research.md` Décision 3)

Un seed minimal dans `pays`, `region`, `ville`, `commune` — une seule ligne par niveau — suffisant
pour rattacher un `id_commune` à un établissement soumis via cette feature. Ce seed n'est pas une
gestion CRUD du référentiel (hors périmètre), seulement une donnée de démarrage.

| Table | Valeur seedée |
|---|---|
| pays | Côte d'Ivoire |
| region | Abidjan |
| ville | Abidjan |
| commune | Cocody |

## Nouvelle configuration technique (pas une entité de données)

- `Admin:ApiKey` — clé statique de protection des endpoints admin (cf. `research.md` Décision 4),
  jamais stockée en base, lue depuis la configuration.
- Chemin de stockage fichier `kyc/{idEtablissement}/{devanture|piece-identite}.<ext>` — stocké comme
  valeur des colonnes `url_photo_devanture`/`url_piece_identite` existantes, pas une nouvelle colonne.
