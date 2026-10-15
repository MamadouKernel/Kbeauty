# Plan: Lot 4 — Rendez-vous

## Backend
- **Scheduling** (domaine) : `Resource`, `ResourceService`, `TimeOff`, `Appointment`, `AppointmentService`, `AppointmentStatusHistory`, `AppointmentStateMachine`, `SlotCalculator` (fonction pure, testée sans base).
- **Catalog** : paramètres de réservation sur `Establishment` (`AutoConfirm`, `MinLeadMinutes`, `MaxHorizonDays`, `CancelLimitMinutes`, `SlotStepMinutes`).
- **Notifications** : `Notification` (in-app + journal SMS), `ISmsSender` (SharedKernel) avec implémentation de journalisation, `IRealtimeNotifier` (SharedKernel) implémenté par l'Api via SignalR.
- **Infrastructure/Scheduling** : `AvailabilityService`, `BookingService` (client), `AgendaService` (partenaire : ressources, indisponibilités, paramètres, RDV, statistiques), `NotificationService`, tâche de fond `AppointmentMaintenance` (expiration, fin de RDV, reprogrammations échues).
- **Api** : endpoints publics (`availability`), client (`/appointments`, `/me/appointments`, `/me/notifications`), partenaire (`/pro/establishments/{id}/...`), hubs SignalR (JWT via `access_token`), migration `AddScheduling` (contrainte d'exclusion GiST, ressource par défaut des établissements existants).
- Tests : unitaires (calcul des créneaux, machine à états) ; intégration (parcours complet, concurrence 409, droits, expiration, notifications).

## Frontend
- Fiche : bouton « Réserver » → `/etablissements/:slug/reserver` (prestations → ressource → calendrier/créneaux rouges temps réel → récapitulatif + téléphone).
- `/compte/rendez-vous` (à venir / passés, annulation, reprogrammation) ; cloche de notifications dans la barre de navigation.
- Partenaire : `/pro/etablissements/:id/agenda` (demandes, agenda, ressources, indisponibilités, paramètres, statistiques), notification temps réel.
- Client SignalR (`@microsoft/signalr`), proxy WebSocket (nginx + dev server).
