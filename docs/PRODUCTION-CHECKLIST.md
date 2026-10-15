# Checklist de mise en production

Légende : ✅ fait dans le code · 🔧 à faire par l'exploitant avant l'ouverture · ⚠️ limite connue acceptée ou à décider.

## Bloquants avant l'ouverture au public

- 🔧 **E-mail** : configurer `Email__Smtp__*` avec votre relais (SendGrid, Brevo, Mailgun, serveur de l'hébergeur…) et un expéditeur dont le domaine a SPF, DKIM et DMARC. Sans relais, personne ne reçoit le lien de confirmation : l'inscription est impossible. Le sender (MailKit) est vérifié contre un relais de test local (Mailpit), pas contre un fournisseur réel.
- 🔧 **Paiement** : obtenir les jetons du compte marchand WiniPayer (TEST puis PROD), passer `Payments__Provider=Winipay` et **dérouler le parcours de DEPLOYMENT.md §7 en bac à sable puis en réel**. Le code suit la documentation et est testé contre un faux serveur, mais n'a jamais parlé à WiniPayer ; le contenu des rappels et la liste des états ne sont pas documentés (voir §7).
- 🔧 **SMS** : aucun fournisseur n'est branché (les SMS sont seulement journalisés). Brancher Infobip ou Twilio derrière `ISmsSender` ; en attendant, les clients ne reçoivent que les notifications dans l'application.
- 🔧 **Textes légaux** : faire relire par un conseil juridique les pages `/cgu` et `/confidentialite` (textes de départ), les compléter avec la raison sociale, l'adresse et le contact du responsable du traitement, et **déclarer les traitements à l'ARTCI**.
- 🔧 **Fonds de carte** : `tile.openstreetmap.org` est réservé à un usage léger ; prendre un fournisseur de tuiles adapté (MapTiler, Stadia…) ou héberger les tuiles, puis ajuster `img-src` dans `docker/security-headers.conf`.
- 🔧 **Secrets** : `Jwt__SigningKey`, mots de passe PostgreSQL et MinIO, clés CinetPay dans un coffre ; retirer `Admin__Email/Password` après le premier démarrage.

## Sécurité

- ✅ HTTPS bout en bout : HSTS posé par nginx et par l'API dès que la requête est en HTTPS (🔧 TLS à fournir devant `web`).
- ✅ En-têtes : CSP stricte, `X-Frame-Options`, `nosniff`, `Referrer-Policy`, `Permissions-Policy`, COOP ; `Cache-Control: no-store` sur l'API.
- ✅ Double authentification obligatoire, verrouillage, sessions révocables, cookie de rafraîchissement httpOnly.
- ✅ Limitation de débit : globale par IP, authentification, réservation (🔧 `Proxy__TrustForwardedHeaders=true` derrière le proxy, sinon tous les visiteurs partagent une IP).
- ✅ Autorisation par rôle et par propriété ; tests d'accès croisés (partenaire A / partenaire B, client / admin).
- ✅ Pièces d'identité : bucket privé, accès admin journalisé, effacées à la suppression du compte.
- ✅ Webhook de paiement : signature, revérification serveur du statut, du montant et de la devise, idempotence.
- ✅ La passerelle `Fake` est refusée au démarrage en `Production`.
- 🔧 `AllowedHosts` au nom de domaine ; ports PostgreSQL, MinIO et API non publiés.
- 🔧 Analyse des dépendances (`dotnet list package --vulnerable`, `npm audit`) dans la CI avant chaque version.
- ✅ Dépendances : `dotnet list package --vulnerable` et `npm audit` sans aucune vulnérabilité connue.
- ✅ Scan dynamique **OWASP ZAP** (mode passif, sur l'interface complète) : 0 échec. Corrigés : version du serveur dans l'en-tête `Server`, jokers `ws:`/`wss:` de la CSP, en-tête `Cross-Origin-Resource-Policy`. Restent, assumés : `style-src 'unsafe-inline'` (styles injectés par Angular et PrimeNG ; passage aux nonces possible plus tard), `Cross-Origin-Embedder-Policy` absent (bloquerait les tuiles de carte), alertes d'information sur les fichiers JavaScript à empreinte (volontairement cacheables).
- ⚠️ Pas de test d'intrusion manuel ni de revue ASVS formelle, et le scan ZAP n'est pas authentifié : à planifier.

## Données personnelles (loi n° 2013-450)

- ✅ Consentement aux CGU et à la politique de confidentialité à l'inscription, avec date conservée.
- ✅ Droit d'accès (export JSON) et droit à l'effacement (anonymisation) depuis « Mon compte » ; consultations de pièces journalisées.
- ✅ Aucun cookie publicitaire ni de mesure d'audience.
- 🔧 Fixer la durée de conservation des dossiers KYC et des factures, et l'appliquer (purge planifiée).

## Performance

- ✅ Bundle initial ≈ 152 Ko compressés (cible 300 Ko), routes chargées à la demande, images WebP, cache long sur les fichiers à empreinte.
- ✅ Index PostGIS et exclusion GiST pour les rendez-vous ; index trigramme sans accent (`keke_unaccent`) pour la recherche textuelle.
- ✅ **Mesuré** sur 50 000 fiches (test `PerformanceTests`, hors réseau, `KEKE_PERF=1 dotnet test --filter PerformanceTests`) : recherche p95 de 16 à 168 ms (budget 300 ms), disponibilités sur 7 jours p95 13 ms (budget 200 ms), liste avec 8 clients en parallèle p95 157 ms. La recherche textuelle était à 1 365 ms avant l'ajout de l'index sans accent.
- ✅ **Mesuré** dans Chrome en mobile bridé (3G rapide, processeur 4× plus lent) sur la page de recherche : CLS 0,00 (0,69 avant correction du pied de page), LCP 4,9 s (5,4 s avant le préchargement de `config.json`). Hors bridage c'est sensiblement plus rapide ; le goulot est le téléchargement et l'exécution du JavaScript.
- ⚠️ Pas de test de charge sur plusieurs machines ni de mesure en production : à refaire sur l'environnement cible.

## Accessibilité (WCAG 2.1 AA)

- ✅ Règles `angular-eslint` d'accessibilité, lien d'évitement, libellés, rôles ARIA, focus visible, contrastes du thème.
- ✅ **Audité avec Lighthouse** (Chrome réel, mobile) : accessibilité 100 sur l'accueil, la fiche, l'inscription, les tarifs, l'espace partenaire (accueil, agenda, abonnement, prestations) et le back-office (finances, utilisateurs, paramètres, journal) ; 99 sur la page de recherche avant la dernière correction (ordre des titres). Défauts corrigés : contraste des chiffres de « Comment ça marche », nom accessible du sélecteur de rayon, ordre des titres de la recherche.
- ⚠️ Pas de test avec un lecteur d'écran (NVDA, VoiceOver, TalkBack) ni d'audit sur les pages de saisie les plus longues (assistant d'inscription d'un établissement) : à faire manuellement.

## PWA

- ✅ Manifeste (nom, couleur `#7433A8`, icônes 192/512), service worker Angular (fichiers statiques seulement ; l'API et SignalR ne sont jamais mis en cache), installable.
- ✅ **Vérifié dans Chrome réel** : service worker enregistré et activé, 107 fichiers en cache, l'application se charge hors ligne depuis le cache, les appels d'API ne sont jamais servis depuis le cache. ⚠️ Pas testé sur un téléphone réel (installation sur l'écran d'accueil).

## Exploitation

- ✅ `/health/live`, `/health/ready`, journaux structurés avec identifiant de requête.
- ✅ Un redéploiement de l'API ne casse plus le site : nginx résout l'adresse de l'API à chaque requête (vérifié en recréant le conteneur sans redémarrer le web).
- ✅ Scripts `scripts/backup-postgres.sh` (sauvegarde compressée vérifiée) et `scripts/verify-restore.sh` (restaure dans une base jetable et compare les volumes de toutes les tables) ; essayés sur la base de développement : 39 tables identiques après restauration, extension PostGIS comprise.
- 🔧 Planifier la sauvegarde quotidienne, **activer l'archivage WAL pour la restauration à un instant donné (PITR)** et rejouer `verify-restore.sh` régulièrement ; versionnement du bucket `private-kyc`.
- 🔧 Supervision et alertes (disponibilité, erreurs 5xx, échecs de paiement, tâches de fond).
- ⚠️ Pas de traces OpenTelemetry ni de backplane Redis pour SignalR (voir DEPLOYMENT.md §5).
- ⚠️ Pas de tests de bout en bout Playwright : les parcours critiques sont couverts par les tests d'intégration et la vérification manuelle dans le navigateur.
