# Tasks: Lot 4 — Rendez-vous

## Backend
- [x] T401 Domaine Scheduling : ressources, indisponibilités, rendez-vous, lignes (instantanés), historique, machine à états
- [x] T402 `SlotCalculator` pur : horaires locaux → UTC, durée totale, pas, délai minimum, horizon, plusieurs ressources
- [x] T403 Réglages de réservation sur l'établissement + migration `AddScheduling` (contrainte d'exclusion GiST, ressource par défaut des fiches existantes)
- [x] T404 Notifications : in-app + SMS (3 tentatives) via `ISmsSender`, push temps réel via `IRealtimeNotifier`
- [x] T405 Services : disponibilités, réservation client (409 si créneau pris, deadlock PostgreSQL inclus), agenda partenaire, ressources, indisponibilités, statistiques
- [x] T406 Tâche de fond : expiration à 24 h, rendez-vous terminés, reprogrammations échues
- [x] T407 Endpoints publics / client / partenaire / notifications ; hubs SignalR (`availability`, `pro`, `notifications`) avec JWT en query string
- [x] T408 Tests : 86 unitaires (créneaux, machine à états, helpers) et 87 d'intégration (parcours, concurrence, droits, expiration, SignalR)

## Frontend
- [x] T411 Services `BookingService`, `AgendaService`, `NotificationService`, `RealtimeService` (SignalR) ; client TypeScript régénéré
- [x] T412 Page de réservation : prestations, professionnel, calendrier (jours indisponibles en rouge), créneaux, récapitulatif ; mise à jour en direct
- [x] T413 `/compte/rendez-vous` (à venir / passés, annulation, réponse à une reprogrammation) et cloche de notifications avec toast
- [x] T414 Agenda partenaire : demandes en direct (alerte visuelle et sonore), vues jour/semaine/mois, ressources, indisponibilités, paramètres, statistiques
- [x] T415 Proxy WebSocket (nginx et serveur de dev) ; tests Vitest (106 au total) ; parcours réel dans le navigateur

## Limites connues
- Plan d'abonnement non contrôlé (lot 5) : le partenaire active la réservation lui-même.
- Heures affichées en Africa/Abidjan (UTC+0) ; saisie des créneaux de reprogrammation sur cette horloge.
- Proposition de reprogrammation soumise aux mêmes règles que la réservation (grille, délai minimum, horizon).
- Modèles de messages dans le code (édition par l'admin au lot 6) ; SMS en journalisation tant qu'aucun fournisseur n'est configuré.
- Agenda sous forme de liste groupée par jour (pas de grille horaire graphique).
- Pas de test Vitest dédié à `BookingPage` ni `AgendaPage` (couvertes par les tests d'intégration et le parcours réel).
