# Feature Specification: Lot 1 — Identité

**Feature Branch**: `002-lot1-identite` | **Created**: 2026-10-11 | **Status**: In progress
**Input**: PRD §7.1 (AUTH-01 à AUTH-03), **amendé** par la demande du 2026-10-11.

## Écarts au PRD (décidés avec le porteur du projet)

| PRD | Décision |
|---|---|
| AUTH-01 : téléphone + OTP SMS | **Remplacé** par e-mail + mot de passe. L'OTP SMS n'est plus dans ce lot. |
| AUTH-02 : 2FA TOTP obligatoire pour les admins | **Étendu** : 2FA TOTP (Google Authenticator) **obligatoire pour tous les comptes**. |
| — | **Connexion Google** (OAuth / Google Identity Services) : implémentée mais **désactivée** par configuration tant que le client ID Google n'est pas fourni. |
| — | **Connexion WhatsApp** : **désactivée**, aucun code d'authentification dans ce lot (seul le drapeau `Auth:WhatsApp:Enabled=false` est exposé). |
| AUTH-01 CA5 : JWT 15 min + refresh 30 jours rotatif | Conservé. Le refresh token est dans un **cookie httpOnly** (le navigateur ne le lit jamais). |

## User Scenarios & Testing

### US1 — Créer un compte avec e-mail et mot de passe (P1)
Je m'inscris (prénom, nom, e-mail, mot de passe, type de compte Client ou Propriétaire d'établissement), je reçois un e-mail de confirmation, je clique le lien.
1. Un mot de passe faible est refusé avec les raisons (≥ 10 caractères, une lettre, un chiffre, sans mon e-mail).
2. Un e-mail déjà inscrit ne révèle rien (même réponse) ; le titulaire reçoit un e-mail l'en informant.
3. Tant que l'e-mail n'est pas confirmé, la connexion est refusée.

### US2 — Se connecter avec 2FA Google Authenticator (P1)
Le mot de passe seul n'ouvre jamais de session.
1. Après e-mail + mot de passe valides : si la 2FA n'est pas encore configurée → écran de configuration (QR code + clé manuelle), validation par un premier code, **10 codes de récupération** affichés une seule fois, puis session ouverte.
2. Si la 2FA est configurée → saisie du code à 6 chiffres (ou d'un code de récupération), puis session ouverte.
3. Un code TOTP ne peut servir qu'une fois (anti-rejeu) ; 5 essais maximum par défi.
4. 5 mots de passe erronés verrouillent le compte 15 minutes.

### US3 — Mot de passe oublié (P2)
Lien de réinitialisation valable 1 h, usage unique ; une réinitialisation révoque toutes les sessions.

### US4 — Session et profil (P1)
Access token JWT 15 min en mémoire ; refresh rotatif (détection de rejeu = révocation de toutes les sessions) ; déconnexion ; liste et révocation des sessions actives ; consultation et mise à jour du profil.

### US5 — Connexion Google (P3, désactivée)
Quand `Auth:Google:Enabled=true` et un `ClientId` fourni : bouton « Continuer avec Google » ; le jeton Google est vérifié côté serveur, le compte est créé ou lié (e-mail vérifié) ; **la 2FA s'applique ensuite comme pour tout compte**.

## Requirements
- **FR-001** Inscription / confirmation / mot de passe oublié / réinitialisation (e-mails via `IEmailSender`, journalisés en développement).
- **FR-002** Connexion en deux étapes (premier facteur → défi 2FA), jamais de jeton après le seul mot de passe.
- **FR-003** TOTP RFC 6238 (SHA-1, 6 chiffres, 30 s, fenêtre ±1), secret chiffré au repos (Data Protection), codes de récupération hachés.
- **FR-004** JWT d'accès signé HS256 (clé ≥ 32 octets, obligatoire au démarrage), refresh token opaque haché, rotation, cookie `kb_rt` httpOnly / SameSite=Strict.
- **FR-005** Limitation de débit sur `/auth/*` et verrouillage de compte.
- **FR-006** Rôles : Client, PartnerOwner, PartnerStaff, Admin, SuperAdmin ; inscription publique limitée à Client et PartnerOwner ; SuperAdmin initial par variables d'environnement.
- **FR-007** `GET /auth/providers` indique les modes de connexion disponibles (Google, WhatsApp).
- **FR-008** Frontend : pages connexion, inscription, confirmation, mot de passe oublié/réinitialisation, 2FA (configuration + saisie), menu compte, guards de routes.

## Success Criteria
- **SC-001** Parcours complet inscription → confirmation → configuration 2FA → connexion → refresh → déconnexion couvert par tests d'intégration.
- **SC-002** Aucun secret, jeton ou mot de passe en clair en base ni dans les logs de production.
- **SC-003** Les endpoints sensibles répondent en ProblemDetails, sans divulguer l'existence d'un compte.
