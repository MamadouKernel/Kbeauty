# Spec: Lot 4 — Rendez-vous

Source : PRD §7.4 (RDV-01 à 06), §7.7 (AGD-01 à 08), §7.9 (notifications), §8.2–8.3, §9, hubs SignalR.
Objectif : réservation → validation partenaire → SMS client ; créneaux rouges en temps réel ; double réservation impossible.

## Adaptations par rapport au PRD
- Les clients se connectent par e-mail (lot 1) : le **téléphone** est saisi au moment de la réservation et sert uniquement au SMS de confirmation. Le SMS passe par `ISmsSender` (implémentation de journalisation en développement, Infobip/Twilio plus tard).
- Les modèles de messages sont dans le code pour l'instant (édition par l'admin au lot 6).
- Le plan d'abonnement n'est pas encore contrôlé (lot 5) : le partenaire active la réservation lui-même, une fois son établissement publié, avec au moins une prestation réservable, une ressource et des horaires.
- Les périodes sont stockées en `starts_at` / `ends_at` (timestamptz) ; la contrainte d'exclusion utilise `tstzrange(starts_at, ends_at)`.
- Vue agenda partenaire sous forme de liste groupée par jour (jour / semaine / mois), pas de grille horaire graphique.

## Règles
- **Créneaux** : horaires d'ouverture − indisponibilités (établissement ou ressource) − RDV actifs (`Pending`, `Confirmed`, `RescheduleProposed`), selon la durée totale des prestations choisies, la grille (pas de 15 min par défaut), le délai minimum (2 h) et l'horizon (60 jours). Une ressource ne propose que les prestations qui lui sont rattachées (aucune = toutes). « Sans préférence » : un créneau est libre si **au moins une** ressource compétente l'est.
- **Aucune double réservation** : contrainte d'exclusion en base ; la violation devient un **409** (« Ce créneau vient d'être réservé »).
- **Machine à états** : `Pending → Confirmed | Rejected | Expired | CancelledByClient` ; `Confirmed → Completed | NoShow | RescheduleProposed | CancelledByClient | CancelledByEstablishment` ; `RescheduleProposed → Confirmed | CancelledByClient`. Chaque transition est historisée.
- **Confirmation automatique** (option), **expiration** d'une demande sans réponse (24 h), RDV terminé → `Completed`, proposition de reprogrammation non traitée avant le nouveau créneau → annulée.
- **Annulation client** jusqu'à X minutes avant (2 h par défaut) ; annulation partenaire toujours possible avec notification.
- **Notifications** : in-app (centre de notifications + temps réel) et SMS (client) à chaque changement ; le partenaire est notifié des nouvelles demandes et annulations.
- **Temps réel** (SignalR) : `/hubs/availability` (`SlotsChanged`), `/hubs/pro` (`AppointmentRequested|Cancelled|Updated`), `/hubs/notifications` (`NotificationReceived`).

## Critères d'acceptation
- Un client réserve, le partenaire valide, le client reçoit SMS et notification in-app.
- Deux réservations simultanées du même créneau : une seule réussit, l'autre reçoit 409.
- Un créneau pris passe en rouge sans rechargement chez les autres visiteurs.
- Un client ne voit et ne modifie que ses RDV ; un partenaire ne gère que ses établissements.
- Les créneaux respectent horaires, indisponibilités, ressources, durée totale, délai minimum et horizon.
