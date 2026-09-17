# RG — Règles de Gestion

Numérotation `RG-<domaine>-<n°>`, traçable vers le CDC (§ référencée entre parenthèses).

## Identité & Comptes
- **RG-ID-01** : Un client MUST créer un compte via numéro de téléphone avec validation OTP par SMS avant toute interaction (appel, RDV). (CDC §3.1)
- **RG-ID-02** : Un compte partenaire MUST passer par le processus KYC (photo devanture + pièce d'identité du gérant) avant activation. (CDC §3.2)
- **RG-ID-03** : Un compte administrateur est créé uniquement par le back-office, jamais par auto-inscription.
- **RG-ID-04** : Un numéro de téléphone est unique par type de compte (un même numéro peut être client ET gérant, mais pas deux comptes clients).

## Localisation & Catégories
- **RG-LOC-01** : Une localisation est hiérarchisée Pays > Région > Ville > Commune ; un établissement MUST être rattaché à une Commune. (CDC §3.1)
- **RG-CAT-01** : Un établissement MUST appartenir à au moins une catégorie (Salon de coiffure, Spa, Massage, Onglerie, etc.). (CDC §3.1)
- **RG-CAT-02** : Seul l'administrateur peut créer/modifier la liste des catégories et zones géographiques. (CDC §5)

## Établissement
- **RG-ETB-01** : Un établissement ne peut publier de fiche visible des clients qu'après validation KYC par l'administrateur. (CDC §5)
- **RG-ETB-02** : Une fiche établissement MUST comporter au moins une prestation avec un tarif pour être publiée.
- **RG-ETB-03** : Le numéro de service client de l'établissement MUST être affiché avec une action "Appeler" directe. (CDC §3.1)
- **RG-ETB-04** : Le bouton "S'y rendre" MUST proposer un itinéraire vers Yango, Google Maps ou Apple Maps à partir des coordonnées GPS exactes de l'établissement. (CDC §3.1, §4)

## Rendez-vous (RDV)
- **RG-RDV-01** : La prise de RDV n'est disponible que si le module réservation est activé pour l'établissement (dépend de l'abonnement). (CDC §3.1, §3.3)
- **RG-RDV-02** : Un créneau déjà réservé ou marqué indisponible MUST apparaître en rouge dans le calendrier client et ne peut pas être sélectionné. (CDC §3.1)
- **RG-RDV-03** : Toute demande de RDV MUST générer une notification (SMS ou in-app) de confirmation ou de refus au client. (CDC §3.1)
- **RG-RDV-04** : Le gérant MUST pouvoir valider, annuler ou reprogrammer une demande de RDV depuis son tableau de bord. (CDC §3.2)
- **RG-RDV-05** : Les disponibilités MUST être synchronisées en temps réel entre le tableau de bord partenaire et le calendrier client (pas de double réservation).

## Abonnement & Paiement
- **RG-ABO-01** : L'accès aux fonctionnalités avancées (ex. module RDV) MUST requérir un abonnement actif de l'établissement. (CDC §3.3)
- **RG-ABO-02** : La durée d'engagement minimale d'un abonnement est de 1 an. (CDC §3.3)
- **RG-ABO-03** : La facturation MUST être modulable : mensuelle ou annuelle, au choix du partenaire. (CDC §3.3)
- **RG-ABO-04** : Le montant des abonnements MUST être ajustable uniquement depuis le back-office administrateur. (CDC §3.3, §5)
- **RG-PAY-01** : Un paiement MUST être effectué via Mobile Money (Wave, Orange Money, MTN, Moov) ou carte bancaire (Visa, Mastercard). (CDC §3.3)
- **RG-PAY-02** : Un abonnement impayé à échéance MUST déclencher une relance de paiement suivie depuis le back-office. (CDC §5)

## Back-office
- **RG-ADM-01** : La validation KYC MUST être effectuée (manuellement ou semi-automatiquement) par un administrateur avant toute activation de compte partenaire. (CDC §5)
- **RG-ADM-02** : Un administrateur peut modérer (suspendre/réactiver) un compte client ou établissement. (CDC §5)
- **RG-ADM-03** : Seul le back-office peut ajouter de nouvelles catégories de beauté ou zones géographiques. (CDC §5)
