# Déploiement de Keke Beauty

Ce document décrit comment mettre l'application en production. La liste de contrôle avant ouverture est dans
[PRODUCTION-CHECKLIST.md](PRODUCTION-CHECKLIST.md).

## 1. Architecture

| Composant | Rôle | Image / technologie |
|---|---|---|
| `web` | Angular (fichiers statiques) + nginx **sans privilèges** (utilisateur `nginx`, port **8080** dans le conteneur) : sert l'application, proxifie `/api`, `/hubs` (WebSocket) et `/health` vers l'API, pose les en-têtes de sécurité (CSP, HSTS derrière TLS) | `src/frontend/keke-beauty-web/Dockerfile` (`nginxinc/nginx-unprivileged:1.30-alpine`) |
| `api` | ASP.NET Core 10 (monolithe modulaire), SignalR, tâches de fond (rendez-vous, facturation) | `src/backend/Dockerfile` |
| `postgres` | PostgreSQL 16 + PostGIS (un schéma par module) | `postgis/postgis:16-3.6` ou service managé avec PostGIS |
| `minio` | Stockage objet S3 (`public-media`, `private-kyc`) | `cgr.dev/chainguard/minio` (développement) ; en production, un service S3 géré ou un MinIO maintenu et épinglé |

**Mise à jour des images de développement (20/10/2026).** PostGIS est passé de `16-3.4` (Debian 11, fin de support) à `16-3.6` (Debian 13), MinIO de l'archive Bitnami à l'image Chainguard, Redis de 7 à 8 et nginx de 1.27 à 1.30 sans privilèges. Si vous aviez une pile locale avant ce changement : (1) **PostgreSQL** : changer de Debian modifie les règles de tri du texte et peut corrompre des index, donc ne réutilisez **jamais** l'ancien volume : `sh scripts/backup-postgres.sh`, démarrez le nouveau service (volume `pgdata_d13`), recréez la base et restaurez (`pg_restore --no-owner`), vérifiez avec `scripts/verify-restore.sh`, puis supprimez `keke-beauty_pgdata` ; (2) **MinIO** : l'image tourne sous l'utilisateur 65532, donnez-lui les données une fois (`docker run --rm --user root -v keke-beauty_miniodata:/data nginx:stable-alpine chown -R 65532:65532 /data`) ; (3) rescannez les images à chaque mise à jour (Trivy, voir §6).

L'API n'est jamais exposée directement : seul `web` reçoit le trafic public, derrière un terminateur TLS
(load balancer, Caddy, Traefik…) qui transmet `X-Forwarded-Proto`.

## 2. Variables d'environnement de l'API

Notation ASP.NET : `Section__Cle` (double souligné). **Aucun secret dans le dépôt** : utilisez un coffre ou les secrets de la plateforme.

| Variable | Obligatoire | Description |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | oui | `Production` (désactive les envois e-mail en clair dans les logs, refuse la passerelle de paiement `Fake`) |
| `ConnectionStrings__Default` | oui | Chaîne Npgsql (utilisateur dédié, mot de passe fort) |
| `DataProtection__KeyEncryptionKey` | oui | Base64 de 32 octets aléatoires (`openssl rand -base64 32`), gardée dans le coffre de secrets : elle chiffre au repos les clés Data Protection de la base. **L'API refuse de démarrer en production sans elle.** En développement : `DATA_PROTECTION_KEK` dans `.env` (généré par `scripts/init-env.sh`) |
| `Jwt__SigningKey` | oui | Clé **aléatoire** de 32 caractères minimum, générée par exemple avec `openssl rand -base64 48` et gardée dans le coffre de secrets. **En production l'API refuse de démarrer** si elle est absente, courte, factice ou si c'est une clé publiée dans le dépôt (public). Changer la clé déconnecte tout le monde. En développement, vide = clé aléatoire propre au processus (`JWT_SIGNING_KEY` dans `.env` pour en fixer une) |
| `Auth__WebBaseUrl` | oui | URL publique du site (liens des e-mails), ex. `https://keke-beauty.ci` |
| `Cors__Origins__0` | si web et API sur des origines différentes | Origine autorisée. Inutile avec le proxy nginx fourni (même origine) |
| `AllowedHosts` | recommandé | Nom d'hôte public, ex. `keke-beauty.ci` (défaut `*`) |
| `Proxy__TrustForwardedHeaders` | oui derrière nginx | `true` : l'API lit l'adresse réelle du client dans `X-Forwarded-For` pour la limitation de débit, **mais seulement si la connexion vient d'un réseau privé** (celui du reverse proxy ; `Proxy__KnownNetworks`, liste de CIDR, remplace les plages par défaut : loopback, RFC 1918, `fc00::/7`, `fe80::/10`). Depuis une adresse publique, l'en-tête est **ignoré**, sinon un `X-Forwarded-For` qui change à chaque requête offrirait un compteur neuf à chaque fois et annulerait toutes les limites. **Ne publiez jamais le port de l'API** : ce garde-fou est une seconde ligne de défense, pas la première |
| `TRUSTED_PROXIES` (conteneur `web`) | derrière un terminateur HTTPS | Adresses ou CIDR du proxy placé **devant** nginx (Caddy, un répartiteur de charge), séparés par des virgules. Sans cela, nginx voit l'adresse de Caddy comme celle du client : **tous vos visiteurs partageraient un seul compteur de limitation** et se bloqueraient entre eux. Avec cela, nginx lit l'adresse du client dans l'en-tête de Caddy, et **uniquement** pour une connexion venant de ces adresses (une entrée invalide est ignorée). Vide = le pair qui se connecte est le client. nginx **réécrit** toujours `X-Forwarded-For` avec l'adresse qu'il a établie lui-même : une valeur fournie par le client ne traverse jamais |
| `Database__MigrateOnStartup` | au premier démarrage / mise à jour | `true` applique les migrations EF au démarrage ; sinon appliquez-les avec `dotnet ef database update` |
| `Database__SeedOnStartup` | au premier démarrage | `true` crée les référentiels (catégories, zones, formules). `Database__SeedDemoData` doit rester `false` |
| `Admin__Email`, `Admin__Password` | au premier démarrage | Crée le premier SuperAdmin (10 caractères minimum, lettre et chiffre). Il configure Google Authenticator à sa première connexion. Retirez ces variables ensuite |
| `Storage__ServiceUrl`, `Storage__AccessKey`, `Storage__SecretKey` | oui | Point d'accès S3 et clés d'un utilisateur limité aux deux buckets |
| `Media__BaseUrl` | oui | URL publique du bucket `public-media` (CDN) |
| `Payments__Provider` | oui | `Winipay` (recommandé) ou `CinetPay`. `Fake` est refusé en production |
| `Payments__Winipay__ApplyKey`, `__Token`, `__Env` | si `Winipay` | En-têtes `X-Merchant-Apply` et `X-Merchant-Token` du compte marchand WiniPayer, et `Env` = `test` (compte TEST) ou `prod` (obligatoire en production). Le jeton signe aussi nos URL de rappel |
| `Payments__CinetPay__ApiKey`, `__SiteId`, `__SecretKey` | si `CinetPay` | Identifiants CinetPay. `SecretKey` signe les notifications |
| `Payments__WebBaseUrl`, `Payments__ApiBaseUrl` | oui | URL publiques du site et de l'API (retour client et notification serveur : `{ApiBaseUrl}/api/v1/webhooks/payments/winipay` doit être joignable depuis Internet) |
| `Email__Smtp__Host`, `__Port`, `__Security`, `__User`, `__Password`, `__FromAddress`, `__FromName` | oui | Relais SMTP (MailKit). `Security` : `StartTls` (port 587, défaut), `SslOnConnect` (port 465) ou `None` (relais de test local). **Sans lui, aucun e-mail (confirmation, mot de passe oublié, relances) n'est envoyé** ; en production l'API le signale au démarrage |
| `Auth__Google__Enabled`, `Auth__Google__ClientId` | non | Connexion Google |
| `Auth__PasswordHashIterations` | non | Itérations PBKDF2-HMAC-SHA512 des nouveaux hachages de mot de passe (défaut **210 000**, minimum imposé : une valeur plus basse est ignorée). Chaque tentative de connexion, même échouée, coûte ce calcul : sur un ordinateur portable modeste elle prend environ 0,7 s, sur un serveur récent environ 0,1 s. **Mesurez la latence sur votre matériel** avant de décider (visez environ 100 ms) |
| `Auth__MaxFailedLogins`, `Auth__LockoutMinutes`, `Auth__TotpMaxLockoutHours` | non | Verrouillage : après `MaxFailedLogins` (5) échecs de mot de passe, le compte est verrouillé `LockoutMinutes` (15). **Le second facteur se verrouille à part et par compte** : 5 mauvais codes (authentificateur ou récupération) le verrouillent 15 minutes, puis le double à chaque récidive jusqu'à `TotpMaxLockoutHours` (24 h) ; un code juste remet tout à zéro. Le mot de passe, lui, n'est pas affecté |
| `RateLimits__GlobalPerMinute`, `RateLimits__BookingPerMinute`, `Auth__AuthPermitPerMinute`, `RateLimits__PaymentRefreshPerMinute` | non | Plafonds par adresse IP (défauts 600, 90 et 30) ; le dernier est **par utilisateur connecté** (40 par minute : relecture du paiement au retour du client) |
| `Billing__GraceDays` | non | Délai de grâce par défaut ; modifiable ensuite depuis le back-office |
| `Billing__MaintenanceEnabled`, `Scheduling__MaintenanceEnabled`, `Reminders__MaintenanceEnabled`, `Retention__MaintenanceEnabled`, `Settlements__MaintenanceEnabled`, `Backups__MaintenanceEnabled` | non | `false` pour désactiver les tâches de fond d'une instance (voir §5) |
| `LeTexto__Enabled`, `__ApiKey`, `__SmsSender`, `__EmailFrom`, `__PublicApiUrl`, `__WebhookSecret`, `Email__Provider` | non | SMS et e-mails via LeTexto, désactivés tant que `Enabled=false` (voir §9) |
| `Push__PublicKey`, `__PrivateKey`, `__Subject` | non | Clés VAPID des notifications push ; vides = paire générée une fois et gardée en base |
| `Redis__ConnectionString` | si plusieurs instances | Backplane SignalR (voir §11). Vide = temps réel limité à l'instance |
| `OpenTelemetry__OtlpEndpoint`, `__Enabled`, `__ServiceName`, `__Protocol` | non | Traces et métriques OTLP (voir §12). Vide = désactivé |
| `Backups__ScheduledEnabled`, `__RetentionDays`, `__IntervalHours`, `__PgDumpPath`, `__TimeoutMinutes` | non | Sauvegardes de la base (voir §10) |
| `Media__MaxVideoMegabytes` | non | Taille maximale d'une vidéo de fiche (défaut 50 Mo ; le proxy accepte 70 Mo par requête) |

Variables du conteneur `web` :

- `YANGO_URL_TEMPLATE` : vide = lien officiel Yango (`yango.go.link/route`, qui ouvre l'application Yango sur un téléphone où elle est installée, avec la destination renseignée, sinon la page web de commande Yango) ; `off` = bouton masqué ; ou un gabarit personnalisé avec `{lat}` et `{lng}`. Demandez un identifiant partenaire à integration-support@yango.com puis remplacez `ref=kekebeauty` si besoin.
- `MAP_TILES_URL` et `MAP_TILES_ATTRIBUTION` : fournisseur de tuiles de carte. Vide = tuiles OpenStreetMap servies **par votre propre site** (`/tiles/`, relais nginx avec cache de 30 jours) : aucun CDN, le navigateur ne contacte jamais un serveur de tuiles tiers. Gratuit et sans garantie de service ; sa politique d'usage interdit un fort trafic (le cache réduit les appels, mais prévoyez un fournisseur dédié à l'ouverture au public). En production, utilisez un fournisseur avec engagement de service (MapTiler, Stadia Maps, Jawg…) : gabarit `https://…/{z}/{x}/{y}…`, par exemple `https://api.maptiler.com/maps/streets-v2/{z}/{x}/{y}.png?key=VOTRE_CLE` ; restreignez la clé à votre domaine (elle est visible dans le navigateur). L'hôte des tuiles est ajouté automatiquement à la politique CSP (`img-src`).

## 3. Mise en route

```bash
# 1. Construire les images (ou laisser la CI le faire)
docker compose build

# 2. Premier démarrage : migrations + référentiels + premier SuperAdmin
Database__MigrateOnStartup=true Database__SeedOnStartup=true docker compose up -d

# 3. Vérifier
curl -f https://keke-beauty.ci/health/ready
```

`docker-compose.yml` est un exemple pour le développement (mots de passe par défaut, MinIO exposé) : en production,
reprenez ses services avec vos secrets, sans publier les ports de PostgreSQL, de MinIO ni de l'API.

## 4. Données et sauvegardes

- **PostgreSQL** : sauvegarde quotidienne **et** archivage WAL pour la restauration à un instant donné (PITR) ; testez une restauration avant l'ouverture. L'application sait aussi faire des sauvegardes complètes (`pg_dump`) depuis le back-office du SuperAdmin : voir §10.
- Les **clés de chiffrement** (Data Protection : secrets TOTP) sont dans la base, **elles-mêmes chiffrées** (AES-256-GCM) par `DataProtection__KeyEncryptionKey` : une copie de la base ou d'une sauvegarde ne suffit donc pas à ouvrir les secrets 2FA. **Conservez cette clé dans le coffre de secrets, et sauvegardez-la séparément de la base** : sans elle, les clés stockées sont illisibles et chaque utilisateur doit réenrôler son authentificateur. Les clés qu'une version antérieure avait stockées en clair sont chiffrées automatiquement au démarrage.
- **MinIO / S3** : activez le versionnement ou la réplication du bucket `private-kyc` (pièces d'identité) ; ce bucket ne doit jamais être public. `public-media` peut être placé derrière un CDN.
- **Rétention** : les pièces d'identité des comptes supprimés sont effacées ; définissez avec votre conseil juridique la durée de conservation des dossiers KYC des partenaires actifs.

## 5. Exploitation

- **Santé** : `/health/live` (processus), `/health/ready` et `/health` (base de données). À brancher sur la supervision du load balancer.
- **Journaux** : JSON compact sur la sortie standard (Serilog), avec identifiant de requête (`X-Request-Id` dans les réponses = `TraceId` des journaux). À collecter par la plateforme (Loki, CloudWatch, ELK…).
- **Mises à jour** : déployer la nouvelle version ; les migrations s'appliquent au démarrage si `Database__MigrateOnStartup=true`. Les migrations sont additives ; faites une sauvegarde avant.
- **Plusieurs instances de l'API** :
  - les tâches de fond (expiration des demandes, facturation) sont idempotentes mais s'exécutent sur chaque instance : pour éviter des notifications en double, désactivez-les (`*__MaintenanceEnabled=false`) sur toutes les instances sauf une ;
  - SignalR utilise un backplane Redis dès que `Redis__ConnectionString` est renseigné (§11) : un message émis par une instance atteint les navigateurs connectés aux autres. Sans Redis, le temps réel n'est cohérent qu'avec une seule instance ;
  - les sauvegardes planifiées (`Backups__ScheduledEnabled`) ne doivent tourner que sur une instance (même règle que ci-dessus).
- **Passerelle de paiement** : déclarez dans CinetPay l'URL de notification ; en cas d'indisponibilité, la tâche de facturation réconcilie les paiements en attente de plus de 30 minutes.

## 6. Sécurité en un coup d'œil

- TLS obligatoire (HSTS posé dès que `X-Forwarded-Proto: https`) ; CSP stricte (`script-src 'self'`) ; `X-Frame-Options: DENY`.
- Double authentification obligatoire pour tous les comptes ; sessions révocables ; verrouillage après échecs.
- Limitation de débit par IP (globale, authentification, réservation) ; autorisation par rôle **et** par propriété.
- Pièces d'identité : bucket privé, accès réservé aux admins, chaque consultation journalisée.
- Droits des personnes : export et suppression de compte depuis « Mon compte ».
- **Le dépôt est public** : aucun secret n'y a sa place (jetons de paiement, clés, mots de passe, même de développement ou de test). Les mots de passe de la pile locale sont générés au hasard dans `.env` (`scripts/init-env.sh`) et les tests génèrent leurs propres secrets à chaque exécution. La pile de développement ne doit **jamais** être exposée (tunnel, réseau partagé, serveur) ; ses ports ne sont publiés que sur `127.0.0.1`. Les anciennes valeurs de développement restent dans l'historique git : elles sont **brûlées**, jamais à réutiliser (la clé JWT d'alors est refusée en production, reconnue par son empreinte).
- **Scans de sécurité** : pour un scanner (OWASP ZAP), lancez l'API avec `docker compose -f docker-compose.yml -f docker-compose.e2e.yml -f docker-compose.scan.yml up -d api` (limites par adresse IP relevées, jeton de 3 h : **jamais ailleurs**). Deux pièges rencontrés : un scanner étranglé par la limite de débit ne teste presque rien (des dizaines de milliers de réponses 429), et un scan « authentifié » dont le jeton expire ou dont la session est révoquée (déconnexion, suppression de compte) devient anonyme sans prévenir : **contrôlez la part de réponses 401 et 429 dans les journaux** avant de conclure.
- **Taille des requêtes** : l'API refuse tout corps de plus de 256 Ko et nginx tout corps de plus de 1 Mo, **sauf** les 4 routes d'envoi de fichiers (photos et vidéos d'établissement, pièces d'identité, photos d'avis) qui ont leur propre plafond (jusqu'à 64 Mo pour les vidéos). Si vous ajoutez une route d'envoi, donnez-lui un `RequestSizeLimit` **et** ajoutez-la à l'expression régulière de `nginx.conf` ; un test vérifie que chaque route d'envoi porte sa limite.
- **Images Docker** : rescannez-les à chaque mise à jour (`docker run --rm -v /var/run/docker.sock:/var/run/docker.sock aquasec/trivy image --severity HIGH,CRITICAL <image>`) ; en production, utilisez des images maintenues et épinglées, un PostgreSQL et un stockage S3 gérés.
- **Où mettre les secrets** : en développement, dans `.env` (ignoré par git, généré par `scripts/init-env.sh`). En production, dans un **coffre de secrets**, jamais dans une image, un fichier versionné ni une variable écrite à la main : l'API lit aussi les secrets **montés en fichiers** (secrets Docker ou Kubernetes), un fichier par paramètre, nommé comme lui avec `__` à la place de `:`, dans `/run/secrets` (dossier modifiable avec `SECRETS_DIR`), par exemple `Jwt__SigningKey`, `ConnectionStrings__Default`, `Payments__Winipay__ApplyKey` et `Payments__Winipay__Token`. Un secret monté en fichier l'emporte sur la variable d'environnement. Ces fichiers sont en lecture seule pour l'utilisateur de l'application. Hors Docker, `dotnet user-secrets` ou le coffre de votre hébergeur (Azure Key Vault, AWS Secrets Manager, HashiCorp Vault) rendent le même service.
- **Contrôle automatique** : `sh scripts/install-hooks.sh` (une fois par copie du dépôt) active un hook qui **refuse tout commit contenant un secret** (gitleaks, via Docker), et la CI fait échouer toute version qui en introduit un (`.gitleaks.toml`). Les 5 anciennes alertes de l'historique sont enregistrées, sans valeur, dans `.gitleaks-baseline.json` : elles sont révoquées et jamais à réutiliser. Activez aussi le *push protection* de GitHub.
- Si GitGuardian (ou un autre outil) signale un secret : le **révoquer et le régénérer chez le fournisseur d'abord**, puis le retirer du code. Effacer l'historique git ne suffit jamais, le dépôt étant public et déjà copiable.

## 7. Brancher WiniPayer (bac à sable, puis production)

`WinipayGateway` suit la documentation WiniPayer (API 2.0, `docs.winipayer.com`) : création d'une facture sur `POST https://api-v2.winipayer.com/checkout/standard/create`, page de paiement hébergée, relecture du statut par `POST .../checkout/standard/detail/{uuid}`.

**État de la vérification (20/10/2026).** Le parcours d'abonnement a été joué de bout en bout contre le **bac à sable** WiniPayer : création de la facture, page de paiement, relecture du statut, activation de l'abonnement, facture payée, notification « Paiement reçu », et changement de formule avec crédit au prorata. **Non vérifié** : le mode PROD, les virements (versements aux établissements et aux collaboratrices) et les remboursements, que WiniPayer ne propose qu'en production (voir « À confirmer avant la production »).

### 7.1 Compte marchand et jetons

1. Créez un compte sur `manager.winipayer.com`, puis un **compte marchand**. Un compte neuf est en environnement **TEST** ; il est **supprimé au bout de 30 jours** s'il n'est pas passé en PROD (« Passer en PROD » dans le Manager, après dépôt des documents demandés).
2. Dans le détail du compte (« Paramètres API »), relevez **deux valeurs seulement** : *Marchand Apply Key* (`X-Merchant-Apply`) et *Test Token Key* ou *Prod Token Key* (`X-Merchant-Token`). Les clés privées (*Test/Prod Private Key*) **ne servent pas** à notre intégration : ne les copiez nulle part.
3. Traitez ces valeurs comme des mots de passe :
   - jamais dans le dépôt, dans une discussion, un ticket ou une capture d'écran ; localement, uniquement dans `.env` (ignoré par git) ;
   - en production, dans un coffre de secrets (le jeton signe aussi nos URL de rappel) ;
   - **régénérez-les dans le Manager dès qu'elles ont été exposées**, et distinguez strictement les jetons TEST et PROD ;
   - changer le jeton invalide les URL de rappel en cours (30 minutes au plus) : faites-le hors des heures de pointe.

### 7.2 Joignabilité de l'API

WiniPayer doit pouvoir appeler l'URL de rappel et renvoyer le client sur le site, et **il refuse les adresses qu'il ne peut pas résoudre** (erreur `cancel_url must be a valid URL`).

- **Essais en local** : un tunnel (cloudflared, ngrok…) vers le conteneur `web` (port 4200), dont l'URL va dans `Payments__ApiBaseUrl` **et** `Payments__WebBaseUrl` (`PAYMENTS_PUBLIC_URL` avec Docker). Un tunnel gratuit change d'adresse à chaque lancement et peut être supprimé côté fournisseur : si WiniPayer répond « service de paiement indisponible » (502), vérifiez d'abord que l'adresse répond encore. Ne l'utilisez jamais pour de vrais paiements.
- **Production** : un nom de domaine stable, en HTTPS (TLS valide devant `web`), avec `Proxy__TrustForwardedHeaders=true` derrière le proxy. Le rappel `{ApiBaseUrl}/api/v1/webhooks/payments/winipay` doit être joignable depuis Internet ; l'API elle-même ne doit pas être publiée directement.

### 7.3 Variables

Variables du fichier `.env` (`docker-compose.yml` les transmet à l'API) :

```
PAYMENTS_PROVIDER=Winipay
WINIPAY_APPLY_KEY=...
WINIPAY_TOKEN=...
WINIPAY_ENV=test
PAYMENTS_PUBLIC_URL=https://<votre-adresse-publique>
```

Hors Docker : `Payments__Provider`, `Payments__Winipay__ApplyKey`, `__Token`, `__Env`, `Payments__ApiBaseUrl` et `Payments__WebBaseUrl`. Puis `docker compose up -d`. Le démarrage échoue explicitement si une clé manque ; **en production, il échoue aussi si `Env` n'est pas `prod`** et si la passerelle simulée `Fake` est choisie.

### 7.4 Parcours à jouer

1. Partenaire avec un établissement approuvé : souscrire depuis `/pro/etablissements/{id}/abonnement` (ou y changer de formule : devis, puis confirmation).
2. Payer sur la page WiniPayer, revenir sur la page abonnement.
3. Vérifier **abonnement actif**, **facture payée**, notification « Paiement reçu ».
4. Jouer aussi un paiement abandonné ou refusé : l'abonnement doit rester en attente et le bouton « Payer » de la facture doit relancer une nouvelle tentative.

### 7.5 Comportement constaté en bac à sable

La documentation WiniPayer ne le décrit pas ; ce sont des observations, à reconfirmer en PROD :

- **Aucun rappel** n'est envoyé à `callback_url` en TEST (aucune requête reçue). Le client est renvoyé sur le site, puis c'est **notre API qui relit le statut** (voir 7.6).
- **Validation automatique** : après « Valider », la page affiche un QR code « à scanner avec l'application WiniPayer Mobile » (non disponible dans les magasins à cette date) ; le paiement TEST se valide seul à la fin du compte à rebours.
- La relecture renvoie `state: success`, `amount_init` (le montant demandé), `currency: xof` et `operator: winipayer`. Les états d'échec ne sont pas documentés : seuls `success` et des états d'échec évidents (`failed`, `cancelled`, `expired`…) sont reconnus, tout autre état reste « en attente ».
- Le lien de paiement expire au bout de **30 minutes** ; chaque clic sur « Payer » crée une **nouvelle facture** WiniPayer (les précédentes expirent seules).
- Frais : le solde TEST est crédité de 99 % du montant (14 850 pour 15 000), alors que la documentation cite 2,5 % de commission dans son exemple. Vérifiez le tarif contractuel (7.8).

### 7.6 Comment le paiement est confirmé (modèle de sécurité)

Nous ne faisons **jamais confiance** au navigateur ni au contenu d'un rappel : seule la relecture du statut chez WiniPayer, authentifiée par vos jetons et faite de serveur à serveur, décide.

- **Rappel** (`/api/v1/webhooks/payments/winipay`, GET ou POST) : notre URL de rappel porte notre identifiant de transaction et sa signature **HMAC-SHA256** (clé : le jeton marchand). Sans signature valide : 401. Avec une signature valide, le rappel ne fait que **déclencher la relecture**.
- **Retour du client** : la page abonnement appelle `POST /api/v1/pro/establishments/{id}/subscription/refresh` toutes les 2 secondes (15 fois au plus). L'API relit alors les paiements en attente de **cet établissement** (propriétaire uniquement, 404 sinon ; 3 au plus, de moins de 24 h ; un seul règlement par facture) et les règle comme un rappel.
- **Réconciliation horaire** : filet de sécurité pour les paiements en attente depuis plus de 30 minutes (client parti avant le retour) ; abandon après 7 jours.
- **Contrôles à chaque règlement** : statut, montant et devise relus chez WiniPayer (un montant différent est **refusé** et journalisé), règlement **idempotent** (un seul appelant gagne), montants entiers en XOF.

### 7.7 Recommandations de sécurité

- Un **compte marchand distinct** par environnement ; ne jamais mélanger les jetons TEST et PROD. Après le passage en production, **supprimez les formules et comptes de test** créés pendant les essais.
- Surveillez les alertes `Refused payment … another amount or currency` (tentative de fraude ou erreur de configuration) et les échecs de joignabilité de WiniPayer (`Could not verify transaction`).
- Limitez l'accès au Manager WiniPayer (mot de passe unique et fort, double authentification si le Manager la propose, personnes strictement nécessaires) : il donne accès aux jetons et aux virements.
- Journalisez, sans jamais y inscrire les jetons ni les URL de rappel signées (le jeton d'un appelant n'a pas à figurer dans les journaux).
- Faites réaliser, avant l'ouverture, un test d'intrusion externe incluant le paiement réel (voir `PENTEST.md`).
- Défense supplémentaire possible, non mise en œuvre : WiniPayer recommande de contrôler le champ `hash` de la relecture (`sha256(clé privée . uuid . crypto . montant . date)`). Notre relecture directe authentifiée offre déjà une garantie équivalente ; le contrôle du `hash` demanderait de stocker la clé privée en plus et n'est utile que si une réponse pouvait être falsifiée en transit.

### 7.8 À confirmer avant la production

À valider auprès du support WiniPayer (questions prêtes dans `docs/WINIPAYER-QUESTIONS-SUPPORT.md`) puis sur un petit montant réel :

- le contenu, la méthode et la signature du rappel en PROD, et la fonction du champ « Lien IPN » du compte marchand (laissé vide : notre URL de rappel attend notre propre signature) ;
- la liste complète des états de facture et de virement ;
- les limites de montant, les frais réels (paiement et virement) et qui les supporte ;
- les virements : adresses IP à déclarer dans la « liste blanche (Payout) » du Manager (3 au plus), opérateurs disponibles, rappel de fin de virement, comportement en cas d'échec ; ils ne sont disponibles qu'en PROD ;
- la procédure et les délais de passage en PROD (documents demandés).

CinetPay reste disponible (`Payments__Provider=CinetPay`, voir ses variables ci-dessus) et suit le même principe de relecture serveur.

## 8. E-mails en développement

`docker compose up` démarre **Mailpit** : tous les e-mails envoyés par l'API (liens de confirmation, mot de passe oublié, relances) arrivent dans la boîte de test http://localhost:8025. Aucun message ne sort vers l'extérieur.

## 9. LeTexto : SMS et e-mails (configuration mise en commentaire)

> **Mis en commentaire le 22 octobre 2026** (`docker-compose.yml` et `.env.example`) : le code LeTexto reste dans l'application mais rien ne le lit tant que les lignes `LETEXTO_*` ne sont pas décommentées. SMS et e-mails retombent sur les journaux et le SMTP de test. WhatsApp passe par Zavu (§14).

LeTexto (https://www.letexto.com) envoie les SMS et peut envoyer les e-mails de compte. **Tout est configuré mais désactivé** : tant que `LeTexto__Enabled=false`, les messages restent dans les journaux (expéditeurs « log »).

1. Dans l'application LeTexto (section « API développeur »), créez la clé d'API → `LETEXTO_API_KEY`.
2. Créez l'expéditeur SMS (11 caractères au plus, validé par LeTexto) → `LETEXTO_SMS_SENDER`, et l'adresse d'expédition e-mail → `LETEXTO_EMAIL_FROM`. Un expéditeur peut aussi être demandé depuis le back-office (Paramètres › Canaux › « Ajouter un expéditeur »).
3. Choisissez un secret long et aléatoire → `LETEXTO_WEBHOOK_SECRET` : il protège l'URL des accusés de réception (`{PublicApiUrl}/api/v1/webhooks/sms/letexto?sig=…`, jointe automatiquement à chaque envoi). `PAYMENTS_PUBLIC_URL` doit être joignable depuis Internet.
4. Passez `LETEXTO_ENABLED=true`. Pour envoyer aussi les e-mails de compte par LeTexto : `EMAIL_PROVIDER=LeTexto` (sinon le relais SMTP reste utilisé).
5. Le back-office (Paramètres › Canaux) affiche l'état de chaque canal et le solde, et permet un SMS de test. L'administrateur active ensuite, canal par canal, SMS / WhatsApp / push (Paramètres › Général).

La documentation LeTexto fournie ne décrit pas de canal WhatsApp : le service WhatsApp est prêt (code de connexion, rappels) mais reste sur l'expéditeur « log » tant qu'un fournisseur WhatsApp Business n'est pas branché (`WhatsApp__Provider`).

## 10. Sauvegardes de la base depuis l'application

Réservé au rôle **SuperAdmin** (Back-office › Sauvegardes, ou `GET/POST /api/v1/admin/backups`).

- **Créer** : exécute `pg_dump` (format personnalisé compressé) et range le fichier dans le bucket privé `private-backups`. L'image de l'API embarque `postgresql-client` ; hors Docker, installez-le ou renseignez `Backups__PgDumpPath`. Le client doit être au moins de la version du serveur PostgreSQL.
- **Télécharger / supprimer** : chaque action est journalisée (`backup.create`, `backup.download`, `backup.delete`).
- **Planification** : `BACKUPS_SCHEDULED=true` crée une sauvegarde quotidienne ; les sauvegardes réussies de plus de `BACKUPS_RETENTION_DAYS` jours (14 par défaut) sont purgées, en gardant toujours les trois plus récentes.
- **Restauration** (volontairement hors de l'application : trop destructrice pour un bouton) :

```bash
# 1. Télécharger le fichier .dump depuis le back-office, puis arrêter l'API.
# 2. Créer une base vide et restaurer :
createdb -h <hote> -U <utilisateur> kekebeauty_restore
pg_restore -h <hote> -U <utilisateur> --no-owner --dbname kekebeauty_restore keke-beauty-AAAAMMJJ-HHMMSS.dump
# 3. Vérifier, puis pointer ConnectionStrings__Default vers cette base (ou renommer les bases) et redémarrer l'API.
```

La sauvegarde contient les clés de chiffrement (Data Protection), **chiffrées** : elles ne s'ouvrent qu'avec `DataProtection__KeyEncryptionKey`, qui n'est jamais dans la base ni dans la sauvegarde ; conservez néanmoins la sauvegarde comme un secret. Les fichiers des buckets (photos, vidéos, pièces d'identité) ne sont **pas** dans le dump : sauvegardez-les séparément (§4).

## 11. Plusieurs serveurs : Redis

Redis n'est **pas nécessaire avec une seule instance de l'API** : laissez `Redis__ConnectionString` vide. Dès qu'il y a plusieurs instances derrière un répartiteur, il sert à deux choses, avec **la même chaîne de connexion sur toutes les instances** :

1. **Temps réel** : SignalR relaie ses messages (agenda, créneaux, notifications) entre instances.
2. **Limites de débit partagées** : sans Redis, chaque instance compte seule, donc une limite de 30 tentatives par minute devient 30 **par instance** (3 instances : 90). Avec Redis, un compteur atomique commun (script Lua, clé `keke:rl:<politique>:<client>:<fenêtre>`, qui expire d'elle-même) applique **une seule** limite. Une même adresse IPv4 écrite `1.2.3.4` ou `::ffff:1.2.3.4` compte pour un seul client.

**Si Redis tombe, le site continue** et les limites aussi : chaque instance revient à son propre compteur (donc une limite par instance, le temps de la panne), un disjoncteur évite d'attendre Redis à chaque requête (10 s), et le compteur `keke_ratelimit_store_errors_total` déclenche l'alerte `SharedRateLimitStoreFailing`. Les délais d'attente sont courts (250 ms) : un Redis lent coûte quelques millisecondes, pas des secondes.

**Sécurité de Redis.** Dans la pile fournie : mot de passe obligatoire (`REDIS_PASSWORD`, généré par `scripts/init-env.sh`), **aucun port publié**, aucune persistance (rien à voler au repos : ce ne sont que des compteurs et des messages éphémères), mémoire plafonnée. En production : un Redis géré ou protégé, avec **TLS** (`host:6380,ssl=true,password=…`), joignable seulement depuis les serveurs de l'API, la chaîne de connexion dans le coffre de secrets. Ne publiez jamais le port 6379 sur Internet.

## 12. Supervision : disponibilité, erreurs, paiements, sécurité

Une pile de supervision **auto-hébergée** est fournie (profil `observability` de `docker-compose.yml`) : rien n'est envoyé à un tiers. Elle contient un collecteur OpenTelemetry, Prometheus, Alertmanager, Grafana, une sonde de disponibilité (blackbox) et Jaeger (traces).

```bash
docker compose --profile observability up -d
# dans .env : OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317 puis redémarrer l'API (docker compose up -d api)
```

| Outil | Adresse (locale) | Rôle |
|---|---|---|
| Grafana | http://localhost:3000 (`admin`, mot de passe `GRAFANA_ADMIN_PASSWORD`) | Tableau de bord « Keke Beauty - overview » (14 panneaux) : disponibilité, erreurs 5xx, latence, paiements, connexions échouées, mémoire |
| Prometheus | http://localhost:9090 | Métriques et règles d'alerte |
| Alertmanager | http://localhost:9093 | Envoi des alertes par e-mail (en développement : dans Mailpit, http://localhost:8025) |
| Jaeger | http://localhost:16686 | Traces : quelle requête est lente, quel appel a échoué |

**Alertes** (`ops/observability/alerts.yml`, chacune dit quoi faire) :

| Alerte | Se déclenche quand | Gravité |
|---|---|---|
| `WhatsAppQuotaReached` | Zavu refuse les envois : quota mensuel (plan Free) ou plafond quotidien atteint | avertissement |
| `WhatsAppTemplateMissing` | un modèle WhatsApp n'est pas approuvé par Meta | avertissement |
| `WhatsAppWebhookSignatureRejected` | plus de 5 appels au webhook Zavu avec une mauvaise signature en 15 minutes | avertissement |
| `SiteOrApiDown` | le site ou l'API ne répond plus à sa sonde de disponibilité pendant 2 minutes (la sonde vérifie aussi la base de données) | critique |
| `ServerErrorsBurst` / `ServerErrorRateHigh` | plus de 10 erreurs 5xx en 5 minutes, ou plus de 5 % des requêtes en erreur | critique |
| `ApiSlow` | 95 % des requêtes dépassent 2 secondes pendant 10 minutes | avertissement |
| `ManyRequestsRefusedByRateLimit` | plus de 200 requêtes refusées par les limites en 10 minutes (attaque, ou limites mal réglées derrière un proxy) | avertissement |
| `PaymentAmountMismatch` | un paiement est refusé parce que le fournisseur annonce un autre montant ou une autre devise (configuration ou fraude) | critique |
| `PaymentsFailing` | 3 paiements échoués ou plus en 30 minutes | avertissement |
| `PaymentProviderErrors` | le fournisseur de paiement est injoignable ou refuse nos appels (clés changées ?) | critique |
| `ForgedPaymentNotifications` | des rappels de paiement avec une signature invalide : quelqu'un sonde l'adresse de rappel | avertissement |
| `SignInFlood` | plus de 50 connexions échouées en 5 minutes | avertissement |
| `SecondFactorLocked` | le second facteur d'un compte vient d'être verrouillé : quelqu'un qui connaît le mot de passe devine les codes | avertissement |
| `SharedRateLimitStoreFailing` | Redis (limites partagées) est en panne : les limites tournent par instance | avertissement |

**Métriques métier** (en plus de celles d'ASP.NET Core, du client HTTP, du runtime .NET et de PostgreSQL) : `keke_payments_settled_total{provider,outcome}`, `keke_payments_notifications_total{provider,result}`, `keke_payments_provider_errors_total{provider,operation}`, `keke_auth_failed_logins_total{reason}`, `keke_auth_failed_second_factor_total`, `keke_auth_second_factor_lockouts_total`, `keke_ratelimit_store_errors_total`, `keke_bookings_created_total`. Elles sont vérifiées par des tests d'intégration (le compteur bouge quand l'événement se produit). Les compteurs ont une latence d'environ une minute (intervalle d'export).

**Avant la production** (non fournis, à décider) :
- **Destinataires des alertes** : remplacez `smtp_smarthost` et `to` dans `alertmanager.yml` par votre relais et l'adresse de l'équipe d'astreinte ; gardez le mot de passe SMTP **hors du fichier** (`smtp_auth_password_file`, monté comme secret). Un canal à réveil réel (SMS, appel) pour les alertes critiques est préférable à l'e-mail seul.
- **Supervision hors de la machine** : si tout tourne sur un seul serveur, une panne du serveur emporte aussi la supervision. Ajoutez un contrôle externe gratuit sur `/health/ready` (UptimeRobot, Better Stack…).
- **Journaux** : ils restent dans `docker logs` (format JSON structuré en production). Pour les conserver et les interroger, ajoutez Loki ; non inclus.
- **Sauvegardes de supervision** : les volumes `promdata` (15 jours) et `grafanadata` ne sont pas sauvegardés.
- **Images** : les images Prometheus, Alertmanager, Grafana et Jaeger sont des versions épinglées, rescannées avec Trivy : elles portent des vulnérabilités élevées dans leurs bibliothèques Go (corrigées en amont, pas encore dans l'image). Elles ne sont publiées que sur `127.0.0.1` ; ne les exposez jamais à Internet, sans authentification forte devant Grafana.
- **Traces** : stockées en mémoire par Jaeger (perdues au redémarrage). Pour les conserver, utilisez Tempo ou le stockage de Jaeger.
- **Paramètres OpenTelemetry** : `OpenTelemetry__OtlpEndpoint` (OTLP gRPC) ou `OpenTelemetry__Protocol=http`. Vide = rien n'est exporté.

## 13. Recherche d'adresse (OpenStreetMap)

Le formulaire d'adresse d'un établissement propose « Chercher l'adresse sur la carte » : l'**API** interroge Nominatim
(OpenStreetMap) ; le navigateur ne contacte jamais un tiers (pas de CDN). Réglages, tous facultatifs sauf le premier en production :

| Variable | Rôle |
|---|---|
| `Geo__UserAgent` | **À renseigner en production** : nom de l'application et moyen de joindre l'exploitant (exigence de la politique d'usage d'OpenStreetMap), par ex. `KekeBeauty/1.0 (contact@votre-domaine.ci)` |
| `Geo__BaseUrl` | Serveur Nominatim (par défaut `https://nominatim.openstreetmap.org`) ; à remplacer par votre propre instance si le volume grandit |
| `Geo__CountryCodes` | Pays où limiter la recherche (par défaut `ci`) |
| `RateLimits__GeoPerMinute` | Recherches par minute et par utilisateur (30) |

Le serveur d'API doit pouvoir sortir en HTTPS vers `nominatim.openstreetmap.org`. Au plus une requête par seconde est envoyée et
les réponses sont gardées 24 h. Les adresses saisies sont transmises à OpenStreetMap : à indiquer dans la politique de confidentialité.

## 14. WhatsApp par Zavu

Zavu (https://docs.zavu.dev) envoie les messages WhatsApp par l'API Cloud de Meta. L'application n'envoie que des **modèles approuvés par Meta** : le code OTP (`keke_otp`, catégorie authentification, bouton « Copier le code ») et les informations (`keke_notification`, catégorie utilitaire, deux variables : titre et message). Au-delà des 24 h de conversation, WhatsApp n'accepte rien d'autre.

### Variables (`.env`, jamais dans le dépôt)
| Variable | Rôle |
|---|---|
| `WHATSAPP_PROVIDER` | `Log` (défaut : rien n'est envoyé) ou `Zavu` |
| `ZAVU_API_KEY` | **Secret.** `zv_test_…` (bac à sable) ou `zv_live_…` (production). En production, une clé de test **empêche le démarrage** |
| `ZAVU_SENDER_ID` | Facultatif : le compte WhatsApp à utiliser s'il y en a plusieurs (en-tête `Zavu-Sender`) |
| `ZAVU_WEBHOOK_SECRET` | **Secret.** Montré **une seule fois** par Zavu à la création du webhook ; obligatoire en production |
| `ZAVU_BASE_URL` | Facultatif (tests) : `https://api.zavu.dev` par défaut |

En production sur le VPS, les secrets peuvent aussi être des fichiers `Zavu__ApiKey` et `Zavu__WebhookSecret` dans le dossier des secrets (`SECRETS_DIR`).

### Mise en route
1. **Compte Zavu** (plan Free, sans carte) : https://zavu.dev. Copiez la clé de test dans `.env` (`ZAVU_API_KEY`) et passez `WHATSAPP_PROVIDER=Zavu`.
2. **Bac à sable** : tableau de bord Zavu › *Sandbox* › *Generate Verification Code*, puis envoyez le message `VERIFY-…` depuis votre WhatsApp. Jusqu'à 5 numéros ; les messages partent pour de vrai, depuis le numéro partagé de Zavu. La clé de test ne sait pas soumettre de modèles (à vérifier au premier essai : l'envoi d'un modèle en bac à sable n'est pas décrit dans la documentation).
3. **Webhook** : tableau de bord Zavu › expéditeur › *Webhooks* : URL `{PAYMENTS_PUBLIC_URL}/api/v1/webhooks/whatsapp/zavu` (HTTPS public ; en local, le même tunnel que WiniPayer), événements `message.delivered`, `message.failed`, `message.inbound`, `template.status_changed`. Copiez le secret affiché dans `ZAVU_WEBHOOK_SECRET`.
4. **Production** : numéro +225 dédié (SIM non inscrite sur WhatsApp) ou application WhatsApp Business (coexistence), entreprise vérifiée chez Meta, moyen de paiement dans le gestionnaire Meta ; puis `ZAVU_API_KEY=zv_live_…`.
5. **Modèles** : `python3 scripts/zavu_templates.py create` les crée et les soumet à Meta (1 à 4 h pour le code, 24 à 72 h pour l'information) ; `python3 scripts/zavu_templates.py status` donne l'état. Un modèle refusé se recrée sous un autre nom (`WhatsApp__OtpTemplate`, `WhatsApp__NotificationTemplate`).

### Ce que fait l'application
- Envoi : `POST /v1/messages` en `channel: whatsapp`, **sans repli SMS automatique** (un code ne doit pas partir par un canal non demandé). Les retours à la ligne, tabulations et longues suites d'espaces sont remplacés par « · » (WhatsApp les refuse).
- Webhook : la signature `X-Zavu-Signature` (HMAC-SHA256, `v2` sur `t.corps`, tolérance 5 minutes) est vérifiée **avant** toute lecture ; corps limité à 64 Ko ; chaque événement n'est traité qu'une fois ; un rapport `delivered` / `failed` met à jour la notification ; une réponse **STOP** d'un numéro connu coupe les notifications WhatsApp de la personne.
- Quota : sur le plan Free, **2 000 messages par mois, envoyés et reçus**. À la limite, l'envoi **et la réception** s'arrêtent jusqu'au 1er du mois ; l'alerte `WhatsAppQuotaReached` prévient. Les notifications retombent alors sur le SMS (s'il est actif) ; les codes OTP par WhatsApp n'arrivent plus : prévoir la connexion par e-mail et mot de passe.
- Coût : aucun supplément Zavu sur WhatsApp ; Meta facture par message selon le pays du destinataire (à vérifier sur la grille Meta).

## 15. E-mails par SMTP (MailKit)

**Tout passe par l'e-mail** : les e-mails de compte (confirmation d'adresse, mot de passe oublié, reçus) **et** les notifications (demande envoyée, rendez-vous confirmé, rappels la veille et 2 h avant, annulation ou report, créneau libéré, invitation à revenir). L'application les envoie par SMTP avec **MailKit** : aucun fournisseur d'API, n'importe quel relais SMTP convient.

| Variable (`.env`) | Rôle |
|---|---|
| `SMTP_HOST`, `SMTP_PORT` | Le relais (en local : `mailpit`, `1025`, la boîte de test http://localhost:8025) |
| `SMTP_SECURITY` | `None` (local), `StartTls` (port 587, le plus courant) ou `SslOnConnect` (port 465) |
| `SMTP_USER`, `SMTP_PASSWORD` | Identifiants du relais ; **`SMTP_PASSWORD` est un secret** : `.env` ou dossier des secrets (`Email__Smtp__Password`), jamais dans le dépôt |
| `SMTP_FROM`, `SMTP_FROM_NAME` | Adresse et nom de l'expéditeur (`noreply@votre-domaine.ci`) |

**Choix du relais** (le domaine d'abord : sans lui, SPF, DKIM et DMARC sont impossibles et les messages tombent dans les spams) :
- un **fournisseur d'envoi** avec SMTP (Brevo, Mailjet, Amazon SES, Resend…) : quelques centaines de messages par jour gratuits, statistiques de remise, c'est le choix recommandé ;
- **Google Workspace** (`smtp.gmail.com`, 587, `StartTls`, mot de passe d'application) : acceptable pour un faible volume, limité à environ 2 000 messages par jour, un compte Gmail gratuit à 500 ;
- le serveur de messagerie de votre hébergeur.

**Réglages côté application**
- Chaque personne choisit dans « Mes notifications » (case **E-mail**) ; les rappels ont leur propre case. Les e-mails de compte partent toujours.
- Le back-office (Paramètres › Général › Canaux de notification) a un interrupteur **E-mail** (activé par défaut).
- Un e-mail n'est envoyé qu'à une adresse **confirmée**, d'un compte **actif**. Les messages de facturation des partenaires gardent leur envoi propre (pas de doublon).
- Un envoi refusé par le relais est réessayé trois fois, puis enregistré comme **échec** (l'historique des notifications le garde) ; une panne du relais ne bloque jamais l'action de la personne.
- Au démarrage en production, un avertissement est écrit si le relais est encore la boîte de test locale.

**À faire avant la production** : domaine, relais, `SMTP_*` dans `.env`, enregistrements SPF, DKIM et DMARC chez votre hébergeur DNS, puis un envoi réel vers une vraie boîte (jamais fait à ce jour : seuls Mailpit et des tests ont reçu des messages).

> WhatsApp (Zavu, §14) et SMS (LeTexto, §9) restent disponibles mais **ne sont plus nécessaires** : le chemin par défaut est l'e-mail. Le code de connexion par WhatsApp est le seul usage sans équivalent par e-mail (la connexion par e-mail et mot de passe reste valable).

