# Tasks: Lot 7 — Durcissement

## Sécurité
- [x] T701 En-têtes de sécurité API (nosniff, X-Frame-Options, Referrer-Policy, Permissions-Policy, COOP, HSTS en HTTPS, no-store sur /api) et identifiant de requête
- [x] T702 IP réelle derrière le proxy (`Proxy:TrustForwardedHeaders`) et limitation de débit globale par IP
- [x] T703 nginx : CSP stricte, HSTS conditionnel, règles de cache du service worker ; critical CSS désactivé (compatible CSP)

## Données personnelles (loi 2013-450)
- [x] T711 Consentement obligatoire à l'inscription, date conservée (`terms_accepted_at`)
- [x] T712 Export des données (`GET /api/v1/me/export`) et suppression de compte par anonymisation (`POST /api/v1/me/delete`), journalisés
- [x] T713 Pages CGU et confidentialité, liens dans le pied de page et à l'inscription, section « Mes données » dans le compte

## PWA et expérience
- [x] T721 Manifeste, icônes 192/512, service worker Angular (statique seulement)
- [x] T722 Bouton Partager (EST-06)

## Exploitation
- [x] T731 Envoi d'e-mails par SMTP (`Email:Smtp:*`)
- [x] T732 `docs/DEPLOYMENT.md`, `docs/PRODUCTION-CHECKLIST.md`, `docs/ETAT-DES-FONCTIONNALITES.md`

## Tests
- [x] T741 4 tests d'intégration (en-têtes, consentement, export, suppression), 111 au total ; Vitest 121

## Non fait (voir la checklist)
- Fournisseur SMS, CinetPay réel, SMTP éprouvé, OpenTelemetry, backplane Redis, test de charge, audit d'accessibilité automatisé, service worker vérifié en navigateur, Playwright.
