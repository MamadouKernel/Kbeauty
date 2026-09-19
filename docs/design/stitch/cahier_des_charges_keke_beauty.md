# Cahier des Charges Fonctionnel : Keke Beauty

## 1. Présentation Générale du Projet
- **Nom de l'application** : Keke Beauty
- **Objectif principal** : Développer une application mobile (et/ou web) servant d'annuaire géolocalisé pour les établissements de beauté, intégrant des modules de mise en relation et de gestion de rendez-vous (RDV).
- **Cible** :
  - **B2C (Clients finaux)** : Personnes recherchant des soins de beauté à proximité.
  - **B2B (Partenaires)** : Salons de coiffure, spas, salons de massage, ongleries, etc.

---

## 2. Charte Graphique et Ergonomie
- **Couleurs dominantes** : Déclinaisons de violet (#5B1B7D, #8A2BE2, teintes lavande/prune pour l'élégance, le luxe et la beauté) et de blanc/crème clair (pureté, clarté, lisibilité).
- **Interface (UI/UX)** : Design épuré, navigation intuitive axée sur la recherche par filtres et la mise en valeur des visuels (galeries photos et formats vidéos courts/stories).
- **Logo** : Intégration du logo officiel Keke Beauty (profil féminin stylisé dans un repère GPS orné d'une fleur).

---

## 3. Spécifications Fonctionnelles

### 3.1. Parcours Client (B2C)
1. **Inscription / Connexion** : Numéro de téléphone avec validation OTP par SMS.
2. **Recherche & Filtres** :
   - Catégories : Salon de coiffure, Espace spa, Massage, Onglerie, Soins esthétiques.
   - Localisation hiérarchique : Pays > Région > Ville > Commune + géolocalisation en temps réel avec carte.
3. **Fiche Établissement** :
   - Médias : Carrousel photo & extraits vidéos (ex: spécialité tresses, nail art).
   - Informations complètes : Description, carte des prestations & grille tarifaire détaillée.
   - Bouton "Appeler" (service client direct).
   - Bouton "S'y rendre" : Redirection / Deep links vers Yango, Google Maps, Apple Maps.
4. **Prise de Rendez-vous** :
   - Calendrier dynamique interactif.
   - Créneaux disponibles vs indisponibles (code couleur rouge distinctif).
   - Confirmation instantanée (SMS / In-app push).

### 3.2. Parcours Partenaire Établissement (B2B)
1. **Onboarding & KYC** :
   - Upload de photo de devanture et profil.
   - Upload de la pièce d'identité du gérant.
   - Renseignement des coordonnées GPS précises, horaires et contacts.
2. **Gestion des prestations** : Ajout, modification de tarifs et durées de service.
3. **Gestion du calendrier & RDV** :
   - Tableau de bord de validation/refus/reprogrammation des réservations.
   - Disponibilité en temps réel.

### 3.3. Monétisation & Abonnements
- Souscription annuelle obligatoire pour les fonctionnalités premium (module de réservation).
- Facturation modulable (mensuelle ou annuelle).
- Moyens de paiement : Mobile Money (Wave, Orange Money, MTN Moov, etc.) et Cartes bancaires (Visa, Mastercard).

---

## 4. Architecture & Back-Office
- Intégrations cartographie (Google Maps/Mapbox + deep links Yango).
- Validation KYC des gérants avant publication.
- Suivi du chiffre d'affaires et modération.