# DD — Dictionnaire des Données

| Code | Libellé | Type | Longueur/Format | Règle de validation | Origine |
|---|---|---|---|---|---|
| id_utilisateur | Identifiant utilisateur | UUID | 36 | Généré système | Système |
| telephone | Numéro de téléphone | Texte | E.164 (+225XXXXXXXXX) | Unique par type de compte, format E.164 | Saisie utilisateur |
| otp_code | Code OTP | Texte | 6 chiffres | Expire après 5 min, usage unique | Système (SMS) |
| type_compte | Type de compte | Enum | CLIENT / PARTENAIRE / ADMIN | Valeur fixe | Système |
| nom | Nom | Texte | 100 | Non vide | Saisie utilisateur |
| id_pays | Identifiant pays | UUID | 36 | Généré système | Référentiel |
| id_region | Identifiant région | UUID | 36 | Rattaché à un pays | Référentiel |
| id_ville | Identifiant ville | UUID | 36 | Rattaché à une région | Référentiel |
| id_commune | Identifiant commune | UUID | 36 | Rattaché à une ville | Référentiel |
| id_categorie | Identifiant catégorie | UUID | 36 | Créé par admin | Back-office |
| libelle_categorie | Libellé catégorie | Texte | 50 | Ex: Salon de coiffure, Spa, Massage, Onglerie | Back-office |
| id_etablissement | Identifiant établissement | UUID | 36 | Généré système | Système |
| nom_etablissement | Nom de l'établissement | Texte | 150 | Non vide | Saisie partenaire |
| description | Description établissement | Texte long | 2000 | Optionnel | Saisie partenaire |
| gps_latitude | Latitude GPS | Décimal | -90 à 90 | Précision 6 décimales | Saisie partenaire (GPS) |
| gps_longitude | Longitude GPS | Décimal | -180 à 180 | Précision 6 décimales | Saisie partenaire (GPS) |
| horaires | Horaires d'ouverture | JSON | — | 7 jours, plages horaires | Saisie partenaire |
| numero_service_client | Numéro affiché "Appeler" | Texte | E.164 | Format E.164 | Saisie partenaire |
| statut_kyc | Statut validation KYC | Enum | EN_ATTENTE / VALIDE / REJETE | Modifié uniquement par admin | Back-office |
| url_piece_identite | Pièce d'identité gérant | Texte (URL) | — | Fichier stocké chiffré | Upload partenaire |
| url_photo_devanture | Photo profil/devanture | Texte (URL) | — | Image JPEG/PNG | Upload partenaire |
| id_media | Identifiant média | UUID | 36 | Généré système | Système |
| type_media | Type de média | Enum | PHOTO / VIDEO | — | Upload partenaire |
| id_prestation | Identifiant prestation | UUID | 36 | Généré système | Système |
| libelle_prestation | Libellé prestation | Texte | 150 | Non vide | Saisie partenaire |
| tarif | Tarif prestation | Décimal | >0 | Devise locale (XOF) | Saisie partenaire |
| duree_minutes | Durée prestation | Entier | >0 | En minutes | Saisie partenaire |
| id_rdv | Identifiant RDV | UUID | 36 | Généré système | Système |
| date_heure_debut | Début du créneau | Timestamp | ISO 8601 | Futur, hors créneau indisponible | Saisie client |
| statut_rdv | Statut du RDV | Enum | DEMANDE / CONFIRME / REFUSE / ANNULE / TERMINE | Transition contrôlée | Système |
| id_abonnement | Identifiant abonnement | UUID | 36 | Généré système | Système |
| periodicite | Périodicité facturation | Enum | MENSUEL / ANNUEL | — | Saisie partenaire |
| date_debut_engagement | Début engagement | Date | ISO 8601 (YYYY-MM-DD) | Durée min. 1 an | Système |
| montant | Montant | Décimal | >0 | Devise locale (XOF), ajustable par admin | Back-office |
| id_transaction | Identifiant transaction | UUID | 36 | Généré système | Passerelle paiement |
| canal_paiement | Canal de paiement | Enum | WAVE / ORANGE_MONEY / MTN / MOOV / VISA / MASTERCARD | — | Saisie utilisateur |
| statut_transaction | Statut transaction | Enum | EN_COURS / REUSSIE / ECHOUEE / REMBOURSEE | Mis à jour via webhook agrégateur | Passerelle paiement |
| date_transaction | Date/heure transaction | Timestamp | ISO 8601 | — | Passerelle paiement |
