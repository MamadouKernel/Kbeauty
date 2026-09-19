# Rapport Global & Spécifications UX/UI : Écosystème Keke Beauty

**Version :** 1.0 (Complet)  
**Date :** 2025  
**Design System :** Imperial Beauty Concierge (`#5B1B7D`, `#8A2BE2`, Lavande, Crème `#FCF9F7`)  
**Marché cible :** Côte d'Ivoire (Abidjan : Cocody, Plateau, Marcory, Riviera, etc.)  
**Paiements & Partenaires clés :** Wave CI, Orange Money, MTN MoMo, Yango, WhatsApp Conciergerie  

---

## 1. Synthèse de l'Écosystème
L'écosystème Keke Beauty se compose de **36 écrans haute fidélité** articulés autour de 6 grands parcours interconnectés :
- **21 Écrans Mobiles** (B2C, B2B Salons, Collaboratrices, Edge Cases)
- **15 Écrans Web Desktop / Tablette Miroir** (Portail Client, Suite Gérant Pro, Tablettes Praticiennes, Super-Admin)

---

## 2. Cartographie Détaillée des 6 Parcours

### Parcours 1 : Expérience Client B2C (Découverte, Réservation & Suivi)
- **Objectif :** Permettre aux clientes de trouver des soins haut de gamme, géolocaliser les salons, choisir leur créneau et régler un acompte sécurisé.
- **Écrans Mobiles (8 écrans) :**
  1. `Connexion & Validation OTP` (Auth rapide SMS +225)
  2. `Découverte & Salons à Proximité` (Feed stories, recherche thématique)
  3. `Carte Interactive des Salons` (GPS plein écran Abidjan)
  4. `Fiche Salon - L'Impératrice Spa` (Médias, prestations, deep link Yango)
  5. `Réservation & Choix du Créneau` (Calendrier interactif)
  6. `Paiement Sécurisé Mobile Money` (Acompte 50% Wave / OM / MoMo)
  7. `Confirmation & Reçu de Paiement` (Pass QR Code, consignes)
  8. `Mes Rendez-vous & Historique` (Suivi en direct, reprogrammation)
- **Écrans Web Desktop (5 écrans) :**
  1. `Accueil & Réservation Web B2C` (Vitrine prestige, recherche géolocalisée)
  2. `Annuaire & Carte Interactive Salons` (Split-view carte/liste interactive)
  3. `Fiche Salon & Réservation Web B2C` (Expérience complète de réservation grand écran)
  4. `Confirmation & Reçu Officiel Web B2C` (Reçu fiscal, QR code HD, bon Yango -15%)
  5. `Mon Espace Client & Historique Web B2C` (Compte à rebours, fidélité Kéké Gold, conciergerie VIP)

---

### Parcours 2 : Résilience, Aléas & Cas d'Exception (Mobile)
- **Objectif :** Supprimer les points de friction locaux (instabilité réseau, retards d'embouteillage à Abidjan).
- **Écrans Mobiles (2 écrans) :**
  1. `Échec Paiement & Récupération Créneau` : Verrouillage du créneau (timer 10 min), diagnostic d'erreur réseau, bascule multi-opérateurs Wave/OM/MoMo/CB, assistance WhatsApp express.
  2. `Gestion Retard, Annulation & Kéké Protect` : Signalement de retard (+10, +15, +30 min) notifiant en direct la tablette de la coiffeuse au miroir ; politique Kéké Protect (remboursement intégral 100% à 4h).

---

### Parcours 3 : Post-Soin, Avis & Pourboires Directs Artisane (Mobile)
- **Objectif :** Valoriser le savoir-faire des tresseuses/esthéticiennes et fidéliser la clientèle.
- **Écran Mobile (1 écran) :**
  1. `Avis Certifié & Pourboire Wave Post-Soin` : Évaluation par critères précis (douceur sans douleur, netteté du traçage, ponctualité), galerie photo Avant/Après (+200 pts Kéké Gold), versement instantané de pourboire Wave à 100% direct sur le numéro personnel de la praticienne (sans commission).

---

### Parcours 4 : Gérance de Salon & Accueil Pro (B2B)
- **Objectif :** Digitaliser la gestion des salons de beauté, maîtriser l'agenda des fauteuils et les encaissements.
- **Écrans Mobiles (7 écrans) :**
  1. `Onboarding & Vérification KYC` (CNI gérant, devanture, localisation GPS)
  2. `Souscription & Abonnement Pro` (Plans annuels, règlement Wave Business)
  3. `Espace Pro - Gestion des Rendez-vous` (Planning mobile de l'équipe)
  4. `Détail Réservation & Validation` (Fiche technique commande, confirmation acompte)
  5. `Gestion des Prestations & Tarifs` (Catalogue, durées, tarification)
  6. `Mon Salon & Gestion d'Équipe` (Rôles, fauteuils attribués, horaires)
  7. `Revenus & Statistiques Pro` (CA journalier, répartition Wave / OM / Espèces)
- **Écrans Web Desktop (3 écrans) :**
  1. `Agenda Multi-Fauteuils & Planning Pro` (Timeline dynamique multi-cabines, minuteur live)
  2. `Réservations & Accueil Comptoir Pro` (Gestion des walk-ins sans RDV, caisse centrale)
  3. `Finances, Acomptes Wave & Catalogue Soins Pro` (Trésorerie globale, virement instantané Wave en 1 clic)

---

### Parcours 5 : Collaboratrices, Coiffeuses & Poste de Travail Tactile
- **Objectif :** Équiper les artisanes sur smartphone et sur tablette tactile au miroir du salon.
- **Écrans Mobiles (3 écrans) :**
  1. `Planning Personnel Collaboratrice` (Liste de ses clientes, alertes)
  2. `Fiche Technique Cliente` (Historique cheveux 4C, sensibilités)
  3. `Mes Commissions & Pourboires` (Solde personnel, virement Wave direct)
- **Écrans Web / Tablette Miroir (3 écrans) :**
  1. `Poste de Travail en Direct & Chrono` (Grand chronomètre temps réel, alerte cuir chevelu sensible, déstockage mèches)
  2. `Fiches Techniques & Diagnostics` (Filtres trichologiques 4C/4B/Défrisé, boissons favorites, historique photos)
  3. `Mes Commissions & Pourboires Wave` (Statistiques hebdomadaires, bulletin récapitulatif certifié PDF)

---

### Parcours 6 : Supervision & Back-Office Super-Admin (Desktop)
- **Objectif :** Gouvernance de la plateforme par l'équipe dirigeante Keke Beauty.
- **Écrans Web Desktop (4 écrans) :**
  1. `Tableau de bord Global Super-Admin` (GMV Abidjan, flux de trésorerie Wave/OM, métriques clés)
  2. `Validation & Audit KYC Salons` (Vérification légale des pièces d'identité et conformité établissements)
  3. `Gestion des Salons & Abonnements` (Renouvellements annuels, relances automatiques)
  4. `Litiges, Acomptes & Conciergerie` (Arbitrage des séquestres, médiation retards/no-show, support client)

---

## 3. Matrice d'Intégration Technique Recommandée
1. **Frontend :** TailwindCSS + Vue/React responsive (breakpoints Tablette 1024px, Desktop 1440px, Mobile 390px).
2. **Backend & API :** Node.js / Python FastAPI avec gestion de webhooks temps réel.
3. **Passerelles Paiement :** API Wave Business (Côte d'Ivoire), Orange Money Web Payment, MTN MoMo API.
4. **Services Externes :** Twilio / SMS Orange pour les OTP, Deep links Yango Partner API, WhatsApp Cloud API pour la conciergerie.
