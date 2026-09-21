# Feature 018 — Extensions à 100% des parcours à scope réduit

## Contexte

Les features 013/014/015/016/017 ont livré un cœur fonctionnel pour 4 parcours (US-20 à US-28) en
documentant explicitement, dans leurs sections Assumptions, les extensions volontairement exclues.
Cette feature ferme ces extensions, une par une, chacune avec un cœur réel (pas de données
fabriquées) :

1. **Parcours 3 — Pourboire direct à la praticienne** (US-29)
2. **Parcours 5 — Compte de connexion collaboratrice** (US-30) : planning individuel, fiches
   techniques clientes, commissions/pourboires en libre-service
3. **Parcours 6 — Litiges** (US-31) : déclaration par client/gérant, instruction et résolution par
   l'administrateur

## Ce qui reste hors périmètre, avec constat honnête

**Parcours 2 — Remboursement automatique réel (virement)** : l'intégration API reste hors de
portée — `WinPayerGateway` (seule intégration de paiement du projet) n'expose que
`POST /checkout/standard/create` et `POST /checkout/standard/detail/{uuid}` côté API WiniPayer,
aucun endpoint de remboursement/payout P2P documenté. En revanche, le **processus** de traitement a
été mis en place (migration 0012) : l'annulation d'un RDV payé ne marque plus `REMBOURSEE`
directement mais crée une demande explicite `REMBOURSEMENT_DEMANDE`, visible dans une file
d'attente administrateur (`/admin/remboursements`) ; l'administrateur effectue le virement/mobile
money manuellement en dehors de la plateforme puis clôture la demande en saisissant sa référence,
ce qui passe le statut à `REMBOURSEE` (avec `reference_remboursement`/`date_remboursement` tracées).
Le client voit la distinction "Remboursement demandé" → "Remboursement effectué".

**Parcours 3 — transfert final du pourboire** : même limitation structurelle. WiniPayer est un
encaisseur marchand pour le compte de Keke Beauty, pas une plateforme de paiement pair-à-pair vers
un tiers individuel (la collaboratrice). Le paiement du pourboire est donc capté sur le même compte
marchand que les RDV (aucune retenue de commission cette fois, contrairement au paiement de la
prestation), et le reversement effectif à la collaboratrice reste un processus manuel/off-plateforme
— documenté explicitement dans l'UI collaboratrice (`StaffPortal.razor`).

**Acomptes et conciergerie** (mentionnés dans le parcours 6 du mockup design) : aucun besoin métier
concret identifié au-delà du paiement en ligne déjà existant (feature 013) ; resteraient à spécifier
si le besoin se confirme.

## Ce qui a été construit (cœur réel, validé end-to-end via curl)

### US-29 — Pourboire
- Migration 0011 : table `pourboire` (id_rdv, id_collaborateur, montant, statut_paiement,
  reference_externe).
- `LaisserPourboireUseCase` réutilise `IPaymentGateway.InitiateAsync` (même intégration WiniPayer
  que le paiement RDV, feature 013).
- `POST /rdv/{id}/pourboire` (identité `X-Client-Id`), vérifie que la collaboratrice appartient au
  même établissement que le RDV.
- Frontend : bouton "Laisser un pourboire" sur un RDV `TERMINE` avec collaboratrice assignée
  (`MesRendezVous.razor`).

### US-30 — Compte collaboratrice
- Migration 0011 : `type_compte_enum` gagne la valeur `COLLABORATEUR` ; `collaborateur` gagne
  `telephone` (unique) et `id_utilisateur` ; `rdv` gagne `id_collaborateur` (assignation) ; nouvelle
  table `fiche_technique` (une par RDV).
- Réutilise intégralement le mécanisme OTP générique (`RequestOtpUseCase`/`VerifyOtpUseCase`, déjà
  paramétré par `TypeCompte` depuis la feature 006) — aucune nouvelle logique d'authentification.
- `LinkCollaborateurCompteUseCase` associe le compte utilisateur nouvellement vérifié à la fiche
  collaboratrice pré-enregistrée par le gérant (même téléphone) ; si aucune fiche ne correspond, le
  statut `compte_non_reference` est renvoyé explicitement plutôt que de créer un accès fantôme.
- `AssignerCollaborateurUseCase` (gérant) et portail `StaffController`/`StaffPortalUseCase`
  (planning, fiche technique, commissions), protégés par `StaffAuthFilter` (en-tête
  `X-Collaborateur-Id`, même principe que `X-Partner-Id`/`PartnerOwnershipFilter`).
- Frontend : 3ᵉ rôle "Collaboratrice" sur `/login`, page `/staff` (`StaffPortal.razor`), sélecteur
  d'assignation sur `PartnerRdv.razor`, champ téléphone sur `PartnerEtablissement.razor`.

### Parcours 2 (extension) — Processus de remboursement
- Migration 0012 : `statut_transaction_enum` gagne `REMBOURSEMENT_DEMANDE` ; `transaction_rdv`
  gagne `reference_remboursement` et `date_remboursement`.
- `IRdvPaiementRepository.DemanderRembourseAsync` (appelé par `AnnulerRdvUseCase`, remplace l'ancien
  `MarquerRembourseAsync` qui marquait `REMBOURSEE` sans aucune trace de traitement réel) place la
  transaction en `REMBOURSEMENT_DEMANDE`.
- `GererRemboursementsUseCase`/`AdminRemboursementController` (`GET/POST /admin/remboursements`,
  `AdminApiKeyFilter`) exposent la file d'attente et le traitement (référence obligatoire).
- Frontend : page `/admin/remboursements`, libellés distincts côté client
  (`MesRendezVous.razor` : "Remboursement demandé" vs "Remboursement effectué").

### US-31 — Litiges
- Migration 0011 : table `litige` (id_rdv, id_utilisateur_declarant, motif, statut_litige,
  resolution).
- `DeclarerLitigeUseCase` (client ou gérant, vérifié partie prenante du RDV), `GererLitigesUseCase`
  (admin, liste + résolution), protégé par `AdminApiKeyFilter` existant.
- Frontend : bouton "Signaler un problème" sur `MesRendezVous.razor`, page `/admin/litiges`.

### Parcours 1 (finalisation) — Notifications in-app et gaps cosmétiques
- Itinéraire multi-options (US-09) : Google Maps, Apple Maps, Yango calculés depuis les GPS réels
  (`EtablissementDetailPage.razor`), au lieu d'un seul lien Google Maps.
- Favoris réels (US-33) : migration 0013 (`favori`), `IFavoriRepository`/`ToggleFavoriUseCase`,
  `POST /etablissements/{id}/favori`, état persisté au lieu d'un simple toggle visuel local.
- Notifications in-app (US-14, migration 0014, `notification`) : `INotificationRepository`,
  `NotificationCenterUseCase`, `NotificationController` (`GET /notifications`,
  `GET /notifications/compteur`, `POST /notifications/lues`). `DecideRdvUseCase` écrit désormais
  une notification à chaque confirmation/refus/reprogrammation de RDV, canal indépendant du
  SMS/WhatsApp (Zavu, bloqué). Composant `NotificationBell.razor` (badge + dropdown), remplace
  l'icône cloche purement décorative de `Home.razor`/`Carte.razor`.
- Bug corrigé au passage : `DecideRdvUseCase.ChangeStatutAsync` envoyait `DateTimeOffset.UtcNow`
  au lieu de la date réelle du RDV dans le message de notification (confirmé/refusé) ; corrigé via
  un nouveau `IRdvRepository.GetDateHeureDebutAsync`. Egalement corrigé au passage : cast Dapper
  `ExecuteScalarAsync<DateTimeOffset?>` échoue sur une colonne `timestamptz` (Npgsql renvoie
  `DateTime`, pas `DateTimeOffset`) - remplacé par un mapping vers une classe dédiée (convention
  déjà appliquée ailleurs dans le projet pour les agrégations Dapper).

### Parcours 3 (finalisation) — Complétion automatique + rappels push (PWA)
- Gap racine découvert et corrigé : aucun mécanisme (job planifié ou action manuelle) ne faisait
  jamais passer un RDV `CONFIRME` à `TERMINE`, ce qui rendait avis et pourboire inaccessibles en
  usage réel (uniquement atteignables via édition directe en base, comme fait pour les tests de
  cette session). `IRdvRepository.MarquerRdvsExpiresCommeTerminesAsync` (UPDATE...RETURNING
  atomique) + `RdvCompletionBackgroundService` (`BackgroundService`, toutes les 5 min) ferment ce
  gap.
- `MarquerRdvsTerminesUseCase` déclenche, pour chaque RDV nouvellement `TERMINE`, une notification
  in-app (toujours) et une notification Web Push (best-effort, si abonnement actif).
- Web Push (VAPID, migration 0015 `push_subscription`) : standard W3C libre, aucun compte tiers à
  configurer (contrairement à Zavu) — paire de clés générée localement pour ce projet.
  `IPushNotificationSender`/`WebPushNotificationSender` (NuGet `WebPush` 1.0.13),
  `IPushSubscriptionRepository`/`PushSubscriptionRepository`, `PushController`
  (`GET /push/vapid-public-key`, `POST /push/subscribe`, `POST /push/unsubscribe`).
- PWA : `sw.js` gère les événements `push`/`notificationclick` (affichage + navigation vers
  `/mes-rendez-vous`), `keke-push.js` (interop `PushManager.subscribe`), composant `PushOptIn`
  proposant l'activation sur `MesRendezVous.razor`.
- Validé end-to-end en conditions réelles (pas de simulation) : RDV confirmé seedé avec échéance
  passée → job de fond détecté automatiquement (poll jusqu'à déclenchement, ~5 min) → statut
  `TERMINE` confirmé → notification in-app créée avec le bon message → tentative d'envoi push
  loggée (échec attendu, l'abonnement de test utilisait une fausse clé de chiffrement — le code
  gère l'échec sans crash ni fausse confirmation).

## Validation

Toutes les capacités ci-dessus ont été validées via curl contre l'API Docker réelle (build 0
erreur/0 avertissement) : ajout collaboratrice avec téléphone → liaison compte (simulée en base,
l'envoi OTP réel restant bloqué par Zavu non configuré comme documenté depuis la feature 003) →
assignation RDV → planning/fiche technique/commissions collaboratrice → déclaration et résolution
de litige → pourboire avec lien de paiement WiniPayer réel généré. Toutes les lignes de test ont été
nettoyées après validation. Page `/admin/litiges` vérifiée en navigateur avec la clé admin réelle.
