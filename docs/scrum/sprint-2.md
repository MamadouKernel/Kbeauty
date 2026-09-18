# Sprint 2 — Inscription et Authentification Client OTP WhatsApp (Keke Beauty)

**Objectif de sprint** : permettre à un client de créer un compte et de se reconnecter par numéro de
téléphone + code OTP envoyé via WhatsApp (Zavu), sans mot de passe.

## Sprint Backlog

| Story | Tâches | Statut |
|---|---|---|
| US-03 — Table OTP | Migration `otp_challenge` créée et appliquée | Fait |
| US-03 — Logique OTP | Génération, hachage SHA-256, invalidation, expiration (5 min) | Fait |
| US-03 — Intégration Zavu | `ZavuWhatsAppOtpSender` (échec explicite si non configuré) | Fait |
| US-03 — Endpoints | `POST /auth/otp/request`, `POST /auth/otp/verify` | Fait |
| US-03 — Test de bout en bout (envoi réel) | Nécessite la configuration du projet Zavu Keke Beauty | **Bloqué (dépendance externe)** |

## Definition of Done du Sprint 2
- [x] Migration `otp_challenge` versionnée et appliquée.
- [x] `POST /auth/otp/request` retourne `502 send_failed` explicite sans Zavu configuré (testé en réel).
- [x] `POST /auth/otp/request` retourne `400 invalid_phone` sur un numéro mal formé (testé en réel).
- [x] `POST /auth/otp/verify` retourne `400 invalid_or_expired_code` sur un code incorrect (testé en réel).
- [x] Une nouvelle demande de code invalide l'ancien challenge `PENDING` (FR-006, testé en réel).
- [ ] Test de bout en bout avec un vrai code WhatsApp — **en attente de la configuration Zavu Keke Beauty**.

## Bug corrigé pendant l'implémentation
Le mapping Dapper d'un `record` positionnel (`OtpChallenge`) provoquait une `InvalidOperationException`
au runtime (constructeur non trouvé). Corrigé en remplaçant le `record` par une classe à propriétés
mutables — validé par test réel après correction (voir `specs/003-auth-client/tasks.md`, T018).

## Prochaine étape
Dès que le projet Zavu Keke Beauty est configuré (clé API réelle dans `.env`), exécuter T017/T018
(sous-cas restants) pour valider le flux de bout en bout. En parallèle, démarrer `004-onboarding-partenaire`
(US-04/US-05, KYC partenaire) ou `005-recherche-annuaire` selon la priorité choisie.
