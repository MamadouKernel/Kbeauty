# Tasks: Lot 1 — Identité

## Backend
- [x] T101 Domaine : User, RefreshToken, EmailToken, LoginChallenge, RecoveryCode (+ configurations EF, migration `AddIdentity`)
- [x] T102 [P] Tests unitaires : PasswordRules, TotpService (fenêtre ±1, anti-rejeu, chiffrement du secret)
- [x] T103 Services : TokenService (JWT + jetons opaques), TotpService, AuthService, AccountService
- [x] T104 Endpoints `/api/v1/auth/*` et `/api/v1/me/*`, cookie refresh, rate limiting, ProblemDetails
- [x] T105 Tests d'intégration : inscription → confirmation → 2FA → session → refresh → logout ; verrouillage ; reset ; Google (faux vérificateur) ; WhatsApp désactivé
- [x] T106 Seed SuperAdmin par variables d'environnement ; configuration JWT validée au démarrage

## Frontend
- [x] T111 AuthService (signals), intercepteur Bearer + refresh, guards `authGuard` / `guestGuard` / `roleGuard`
- [x] T112 Pages : connexion, inscription, confirmation e-mail, mot de passe oublié / réinitialisation
- [x] T113 Pages 2FA : configuration (QR + codes de récupération) et saisie du code
- [x] T114 Menu compte dans l'en-tête, page « Mon compte » (profil, sessions)
- [x] T115 Bouton Google conditionné à `/auth/providers` (WhatsApp absent) — **non testé contre Google réel** (pas de client ID)
- [x] T116 Tests Vitest + parcours réel dans le navigateur via docker compose

## Reste à faire / limites connues
- Envoi d'e-mails réel (SMTP/Mailpit) : prévu avec le module Notifications ; les liens sont journalisés en développement.
- Clés Data Protection stockées en base sans chiffrement de clé : à renforcer au lot 7 (certificat ou KMS).
- Connexion Google : à activer et valider avec un vrai client OAuth.
- Redémarrage conseillé de l'API dans Visual Studio après ce lot (nouvelle migration `AddIdentity`, clé JWT de développement).
