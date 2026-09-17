# MCT — Modèle Conceptuel des Traitements

Notation : Événement déclencheur → Opération (règles appliquées) → Résultat / Événement produit.

## Processus 1 : Inscription & Authentification (Client)
1. Événement : *Demande d'inscription* (numéro de téléphone saisi)
2. Opération : Génération OTP, envoi SMS (RG-ID-01)
3. Événement : *OTP reçu* → Opération : Vérification code (expiration 5 min)
4. Résultat : Compte CLIENT créé et actif — ou Échec (code invalide/expiré)

## Processus 2 : Onboarding Partenaire (KYC)
1. Événement : *Demande d'inscription établissement*
2. Opération : Saisie infos de base + upload photo devanture + pièce d'identité (RG-ID-02)
3. Événement : *Dossier soumis* → Opération : Mise en file d'attente KYC (statut EN_ATTENTE)
4. Événement : *Revue admin* → Opération : Validation ou rejet (RG-ADM-01)
5. Résultat : Établissement VALIDE (fiche publiable) ou REJETE (notification au partenaire)

## Processus 3 : Recherche & Consultation Établissement (Client)
1. Événement : *Recherche lancée* (catégorie + localisation)
2. Opération : Filtrage établissements VALIDE, actifs, correspondant aux critères (RG-CAT-01, RG-LOC-01)
3. Résultat : Liste de fiches établissement affichée
4. Événement : *Consultation fiche* → Opération : Chargement médias/prestations/tarifs
5. Résultat : Actions possibles — Appeler, S'y rendre (RG-ETB-03, RG-ETB-04), Prendre RDV (si abonnement actif)

## Processus 4 : Prise de Rendez-vous
1. Événement : *Sélection créneau* par le client
2. Opération : Vérification disponibilité (créneau non rouge/indisponible) (RG-RDV-02)
3. Résultat : RDV créé en statut DEMANDE → notification envoyée à l'établissement
4. Événement : *Décision gérant* (valider/refuser/reprogrammer) (RG-RDV-04)
5. Opération : Mise à jour statut_rdv, synchronisation calendrier (RG-RDV-05)
6. Résultat : Notification de confirmation ou de refus au client (RG-RDV-03)

## Processus 5 : Souscription & Paiement d'Abonnement
1. Événement : *Souscription initiée* par l'établissement (choix périodicité) (RG-ABO-03)
2. Opération : Création ABONNEMENT (statut ACTIF sous réserve de paiement), engagement ≥ 1 an (RG-ABO-02)
3. Événement : *Paiement déclenché* (Mobile Money ou carte) (RG-PAY-01)
4. Opération : Appel agrégateur de paiement, création TRANSACTION (EN_COURS)
5. Événement : *Webhook agrégateur* → Opération : Mise à jour statut_transaction
6. Résultat : Abonnement ACTIF (paiement réussi) ou IMPAYE → relance programmée (RG-PAY-02)

## Processus 6 : Modération & Paramétrage (Back-office)
1. Événement : *Anomalie signalée ou contrôle périodique*
2. Opération : Suspension/réactivation compte (RG-ADM-02)
3. Événement : *Besoin de nouvelle catégorie/zone*
4. Opération : Création par admin (RG-ADM-03, RG-CAT-02)
5. Résultat : Référentiel catégories/zones mis à jour, visible immédiatement en recherche
