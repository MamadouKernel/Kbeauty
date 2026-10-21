# Checklist de mise en production

Légende : ✅ fait dans le code · 🔧 à faire par l'exploitant avant l'ouverture · ⚠️ limite connue acceptée ou à décider.

## Bloquants avant l'ouverture au public

- 🔧 **E-mail** : configurer `Email__Smtp__*` avec votre relais (SendGrid, Brevo, Mailgun, serveur de l'hébergeur…) et un expéditeur dont le domaine a SPF, DKIM et DMARC. Sans relais, personne ne reçoit le lien de confirmation : l'inscription est impossible. Le sender (MailKit) est vérifié contre un relais de test local (Mailpit), pas contre un fournisseur réel.
- 🔧 **Paiement (WiniPayer)** : le parcours d'abonnement a été joué de bout en bout sur le **bac à sable** (20/10/2026). Reste à : passer le compte marchand en **PROD** (documents demandés par WiniPayer ; le compte TEST est supprimé après 30 jours), utiliser les jetons PROD (`Payments__Provider=Winipay`, `Payments__Winipay__Env=prod`), exposer l'API sur un **domaine stable en HTTPS**, puis **jouer un paiement réel de petit montant** et rejouer le parcours de DEPLOYMENT.md §7. Voir la section « Paiement WiniPayer » ci-dessous.
- 🔧 **SMS** : le client **LeTexto** est écrit mais **désactivé** (`LETEXTO_ENABLED=false`) et n'a jamais envoyé de message réel ; sans lui les SMS sont seulement journalisés et les clients ne reçoivent que les notifications dans l'application. Activer LeTexto (DEPLOYMENT.md §9) et valider un envoi réel, ou brancher un autre fournisseur derrière `ISmsSender`.
- 🔧 **Textes légaux** : faire relire par un conseil juridique les pages `/cgu` et `/confidentialite` (textes de départ), les compléter avec la raison sociale, l'adresse et le contact du responsable du traitement, et **déclarer les traitements à l'ARTCI**.
- 🔧 **Fonds de carte** : `tile.openstreetmap.org` est réservé à un usage léger ; prendre un fournisseur de tuiles adapté (MapTiler, Stadia…) ou héberger les tuiles, puis ajuster `img-src` dans `docker/security-headers.conf`.
- ✅ **Dépôt public, aucun secret versionné** : la clé JWT de développement a été retirée du code (clé aléatoire par processus en développement) et l'API **refuse en production** une clé absente, courte, factice ou publiée ; ports de la pile locale limités à `127.0.0.1`. Les anciennes valeurs de développement restent dans l'historique git : à considérer comme **connues de tous**, jamais à réutiliser ailleurs. 🔧 Activer le **push protection** des secrets de GitHub sur le dépôt, et ne jamais exposer la pile de développement.
- ✅ **Clés Data Protection chiffrées au repos** : la clé qui chiffre les secrets TOTP n'est plus stockée en clair dans la base, mais chiffrée (AES-256-GCM) par une clé du coffre ; l'API refuse de démarrer en production sans elle, et convertit seule les clés déjà stockées en clair. 🔧 Mettre `DataProtection__KeyEncryptionKey` dans le coffre et la **sauvegarder séparément** de la base (la perdre oblige chaque utilisateur à réenrôler son authentificateur) ; prévoir une procédure de rotation.
- 🔧 **Secrets** : `Jwt__SigningKey`, mots de passe PostgreSQL et MinIO, **jetons WiniPayer (PROD)**, clés CinetPay et LeTexto dans un coffre, jamais dans le dépôt ni dans une discussion ; retirer `Admin__Email/Password` après le premier démarrage. **Régénérer** toute clé qui a été affichée ou partagée (cas des clés WiniPayer manipulées pendant les essais du 20/10/2026).

## Paiement WiniPayer

- ✅ Création de facture, page hébergée, relecture du statut, activation de l'abonnement, facture payée, notification et changement de formule avec prorata : **vérifiés sur le bac à sable**.
- ✅ Confirmation sûre : rappel signé (HMAC) qui ne fait que déclencher une relecture, retour du client qui relit lui aussi chez WiniPayer (propriétaire uniquement, 3 paiements au plus, 24 h), réconciliation horaire, statut, montant et devise toujours relus, règlement idempotent, un montant différent est refusé.
- ✅ Garde-fous au démarrage : jetons obligatoires, `Env=prod` imposé en production, passerelle `Fake` refusée.
- ⚠️ **Constaté en TEST** : aucun rappel n'est envoyé et le paiement se valide automatiquement (l'application mobile WiniPayer n'est pas encore publiée). À reconfirmer en PROD.
- 🔧 Passage en PROD, domaine HTTPS stable, jetons PROD en coffre, **clés régénérées**, séparation stricte TEST/PROD.
- 🔧 **Virements et remboursements** (versements aux établissements et aux collaboratrices, remboursements) : disponibles seulement en PROD chez WiniPayer, jamais exercés. Déclarer l'adresse IP sortante du serveur dans la « liste blanche (Payout) » du Manager (3 adresses au plus), puis tester avec un montant minimal.
- 🔧 Obtenir par écrit du support WiniPayer : contenu et signature du rappel, liste des états, limites de montant, frais réels et qui les supporte (voir `docs/WINIPAYER-QUESTIONS-SUPPORT.md`).
- 🔧 Alertes sur `Refused payment … another amount or currency` et sur les échecs de joignabilité du fournisseur ; accès au Manager WiniPayer réservé et protégé (mot de passe fort, double authentification si disponible).
- ⚠️ Contrôle du champ `hash` recommandé par WiniPayer non mis en œuvre : la relecture directe authentifiée offre déjà une garantie équivalente (DEPLOYMENT.md §7.7).

## Services externes : inventaire

| Service | Rôle | État dans le code | À faire avant l'ouverture |
|---|---|---|---|
| **WiniPayer** | Paiement des abonnements, virements | ✅ Bac à sable vérifié | PROD, domaine, premier paiement réel, virements (voir ci-dessus) |
| **Relais SMTP** (SendGrid, Brevo, Mailgun…) | E-mails de compte (confirmation, mot de passe oublié) | ✅ Client MailKit, vérifié contre Mailpit seulement | **Bloquant** : relais réel, domaine avec SPF, DKIM, DMARC, test d'envoi réel |
| **LeTexto** | SMS et e-mails | 🟡 Client écrit, **désactivé** | Clé API, expéditeur, `LETEXTO_ENABLED=true`, envoi réel, `LETEXTO_WEBHOOK_SECRET` |
| **WhatsApp Business** | Code de connexion, notifications | ⏸ Désactivé, **aucun fournisseur branché** | Choisir un fournisseur, implémenter `IWhatsAppSender`, modèles de messages validés |
| **Fournisseur de tuiles de carte** (MapTiler, Stadia…) | Fonds de carte | 🟡 Tuiles OSM/CARTO par défaut, usage léger seulement | Fournisseur avec engagement, clé restreinte au domaine, `img-src` |
| **Stockage S3 / MinIO** | Photos, pièces d'identité | ✅ Code prêt | Instance de production, utilisateur limité, buckets (`private-kyc` privé et versionné), sauvegarde |
| **Google (connexion)** | Connexion optionnelle | ✅ Code prêt, désactivé par défaut | Identifiant client OAuth si souhaité |
| **Service push du navigateur (VAPID)** | Notifications push | 🟡 Côté serveur et client écrits, **jamais testé en réel** | Test sur HTTPS avec un vrai navigateur |
| **Redis** | Temps réel sur plusieurs instances | ✅ Testé (20/10/2026) : deux serveurs, notification relayée, canaux SignalR actifs | Nécessaire dès que l'API tourne sur plus d'une instance ; mot de passe et TLS sur le Redis de production |
| **OpenTelemetry (collecteur)** | Traces et métriques | ✅ Traces testées vers Jaeger (20/10/2026) ; métriques non vérifiées | Choisir un collecteur, vérifier les métriques, définir des alertes (disponibilité, 5xx, échecs de paiement) |
| **Domaine, TLS, proxy** | Accès public | 🔧 Hors code | Nom de domaine, certificat, `Proxy__TrustForwardedHeaders=true`, `AllowedHosts` |
| **Sauvegardes et PITR** | Reprise sur incident | ✅ Scripts et sauvegarde depuis l'application testés, restauration vérifiée (20/10/2026) | Planification, archivage WAL (PITR), test de restauration régulier, sauvegarde séparée des buckets |
| **Yango** | Lien d'itinéraire | ✅ Lien officiel | Identifiant partenaire (facultatif) |

## Sécurité

- ✅ HTTPS bout en bout : HSTS posé par nginx et par l'API dès que la requête est en HTTPS (🔧 TLS à fournir devant `web`).
- ✅ En-têtes : CSP stricte, `X-Frame-Options`, `nosniff`, `Referrer-Policy`, `Permissions-Policy`, COOP ; `Cache-Control: no-store` sur l'API.
- ✅ Double authentification obligatoire, verrouillage, sessions révocables, cookie de rafraîchissement httpOnly.
- ✅ **Second facteur verrouillé par compte** : 5 mauvais codes le verrouillent 15 minutes, doublé à chaque récidive jusqu'à 24 h (`Auth__TotpMaxLockoutHours`) ; les mots de passe courants sont refusés ; hachage PBKDF2 à 210 000 itérations.
- ✅ **Images maintenues et scannées** (Trivy) : API, site (nginx sans privilèges), Redis, MinIO sans vulnérabilité élevée ou critique ; PostGIS 25 avec correctif (utilitaire `gosu`). 🔧 En production : PostgreSQL et stockage S3 gérés, images épinglées par empreinte, `Dependabot` activé sur le dépôt, rescan à chaque mise à jour.
- 🔧 **Rotation des clés** (jetons de session, clé de chiffrement du keystore) : écrire et tester la procédure ; **alertes** sur les verrouillages 2FA, les connexions en rafale et les montants refusés (voir `docs/ASVS-L2.md`).
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
