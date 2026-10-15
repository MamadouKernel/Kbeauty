# Tests de bout en bout (Playwright)

Un vrai navigateur (Edge, déjà installé sous Windows) pilote le site comme le ferait un client, un partenaire ou un administrateur,
contre la pile Docker complète (web, API, PostgreSQL, MinIO, Redis, Mailpit).

## Lancer

```bash
# 1. La pile, avec des limites de débit relevées pour la suite (elle crée des dizaines de comptes depuis une seule adresse IP)
docker compose -f docker-compose.yml -f docker-compose.e2e.yml up -d --build

# 2. Les tests
cd e2e
npm install
npx playwright test                       # tout : ordinateur (1280 px) puis téléphone (Pixel 7, 412 px)
npx playwright test --project=mobile      # l'émulation téléphone seule
npx playwright test tests/security.spec.ts
npx playwright show-report                # rapport HTML, avec captures et traces des échecs
```

Variables : `E2E_BASE_URL` (défaut `http://localhost:4200`), `E2E_MAILPIT_URL` (défaut `http://localhost:8025`),
`E2E_BROWSER_CHANNEL` (défaut `msedge` ; `chrome`, ou vide après `npx playwright install chromium`).

## Ce que couvre la suite

| Fichier | Parcours |
|---|---|
| `public.spec.ts` | accueil, annuaire, fiche (itinéraire Yango avec coordonnées), recherche, textes légaux, en-têtes de sécurité |
| `a11y.spec.ts` | WCAG 2.1 A/AA + bonnes pratiques (axe-core) sur 10 pages publiques |
| `pwa.spec.ts` | manifeste installable, service worker, ouverture hors-ligne, pas de défilement horizontal, barre d'onglets du téléphone |
| `auth.spec.ts` | inscription, confirmation par e-mail (Mailpit), activation de Google Authenticator, reconnexion, déconnexion après inactivité (horloge simulée), préférences de notification |
| `booking-payment.spec.ts` | réservation d'un salon de démonstration, acompte payé en ligne, paiement refusé, annulation avec remboursement |
| `backoffice.spec.ts` | paramètres (inactivité, conservation des pièces), rédaction et publication d'un texte légal, **sauvegarde réelle de la base (pg_dump) téléchargée**, droits du SuperAdmin, canaux, reversements |
| `videos.spec.ts` | import d'une vidéo (stockage MinIO réel), ordre photos puis vidéos, refus d'un faux fichier, formule gratuite |
| `security.spec.ts` | tests d'intrusion rejoués (OWASP Top 10) : accès, IDOR, jetons falsifiés, injections, XSS, envois de fichiers piégés, CORS, webhooks falsifiés, limitation de débit |

Les comptes sont créés par l'API (inscription, confirmation par le courrier capté par Mailpit, double authentification avec un code
TOTP calculé par la suite). La seule opération faite hors API est la promotion d'un compte jetable en Admin/SuperAdmin, par une requête SQL
dans le conteneur PostgreSQL (aucune API ne le permet, volontairement).

## Ce que la suite ne fait pas

- Pas de vrai paiement ni de vrai SMS : la passerelle simulée et les expéditeurs « journal » sont utilisés.
- Les notifications push réelles (service push du navigateur) et l'ouverture de l'application Yango sur un vrai téléphone ne sont pas testables ici.
- Pas de lecture d'écran (hors périmètre décidé) ni de test de charge (réalisé séparément).
