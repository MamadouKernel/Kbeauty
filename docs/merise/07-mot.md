# MOT — Modèle Organisationnel des Traitements

Pour chaque processus du MCT (`06-mct.md`), répartition Qui / Où / Quand / Comment (manuel ou automatisé).

| Processus | Étape | Acteur / Poste | Lieu | Moment | Nature |
|---|---|---|---|---|---|
| P1 Inscription Client | Saisie téléphone, réception OTP | Client | App mobile/web | Temps réel | Automatisé |
| P1 Inscription Client | Vérification OTP | Système | Serveur (API + SMS gateway) | Immédiat (< 5 min) | Automatisé |
| P2 Onboarding Partenaire | Upload pièces (devanture, ID) | Gérant établissement | App/Web partenaire | À l'inscription | Manuel (saisie) + automatisé (upload) |
| P2 Onboarding Partenaire | Revue et validation KYC | Administrateur back-office | Back-office web | Sous 48h (à définir en Sprint 0) | Manuel |
| P3 Recherche établissement | Filtrage catégorie/localisation | Client | App mobile/web | Temps réel | Automatisé |
| P3 Consultation fiche | Affichage médias/tarifs/actions | Client | App mobile/web | Temps réel | Automatisé |
| P4 Prise de RDV | Sélection créneau | Client | App mobile/web | Temps réel | Automatisé |
| P4 Prise de RDV | Validation/refus/reprogrammation | Gérant établissement | Tableau de bord partenaire | Sous délai défini par l'établissement | Manuel |
| P4 Prise de RDV | Notification résultat | Système | SMS / In-app | Immédiat après décision gérant | Automatisé |
| P5 Souscription abonnement | Choix périodicité, paiement | Gérant établissement | App/Web partenaire | À la souscription / échéance | Manuel (choix) + automatisé (paiement) |
| P5 Paiement | Traitement transaction | Agrégateur de paiement (CinetPay/PaySika/TouchPay) | Passerelle externe | Temps réel | Automatisé |
| P5 Relance impayé | Suivi et relance | Administrateur back-office | Back-office web | Selon échéancier | Manuel + notifications automatisées |
| P6 Modération | Suspension/réactivation compte | Administrateur back-office | Back-office web | Sur signalement/contrôle | Manuel |
| P6 Paramétrage référentiel | Ajout catégorie/zone/tarif abonnement | Administrateur back-office | Back-office web | Selon besoin métier | Manuel |

## Notes de découpage Scrum
Ce tableau MOT nourrit directement le Product Backlog (`docs/scrum/product-backlog.md`) :
chaque ligne "Automatisé" ou "Manuel + automatisé" correspond à une ou plusieurs User Stories.
