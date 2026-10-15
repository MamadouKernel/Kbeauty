# Implementation Plan: Lot 1 — Identité

**Spec**: [spec.md](spec.md) | **Constitution**: `.specify/memory/constitution.md`

## Technical Context
- Module `KekeBeauty.Modules.Identity` : domaine, services applicatifs, endpoints Minimal API, configurations EF (schéma `identity`).
- Le module ne référence pas l'Infrastructure : il dépend de l'interface `IIdentityDbContext` que `KekeBeautyDbContext` implémente.
- Paquets : `Microsoft.Extensions.Identity.Core` (PasswordHasher PBKDF2), `Otp.NET` (TOTP), `Microsoft.AspNetCore.Authentication.JwtBearer`, `Google.Apis.Auth` (vérification du jeton Google), `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` (clés persistées en base, sinon les secrets TOTP deviendraient illisibles à chaque redémarrage de conteneur).
- Frontend : Angular signals, intercepteur HTTP, guards, `qrcode` pour le QR de configuration 2FA.

## Décisions
1. **Premier facteur → défi** : `/auth/login` renvoie toujours un `challengeToken` (purpose `TwoFactor` ou `TwoFactorSetup`) ; seule la validation du code ouvre une session.
2. **Anti-énumération** : inscription et mot de passe oublié répondent identiquement que l'e-mail existe ou non.
3. **Refresh token en cookie httpOnly** (`Path=/api/v1/auth`, SameSite=Strict) ; access token en mémoire côté Angular, restauré au chargement par `/auth/refresh`.
4. **Horloge injectée** (`TimeProvider`) partout (TOTP, expirations, verrouillage) pour des tests déterministes (`FakeTimeProvider`).
5. **Limites connues V1** : clés Data Protection stockées en base sans chiffrement de clé (à renforcer au lot 7 : certificat ou KMS) ; `IEmailSender` journalise seulement (SMTP réel + Mailpit au lot 4 avec les notifications).
