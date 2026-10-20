# Déploiement de Keke Beauty

Ce document décrit comment mettre l'application en production. La liste de contrôle avant ouverture est dans
[PRODUCTION-CHECKLIST.md](PRODUCTION-CHECKLIST.md).

## 1. Architecture

| Composant | Rôle | Image / technologie |
|---|---|---|
| `web` | Angular (fichiers statiques) + nginx : sert l'application, proxifie `/api`, `/hubs` (WebSocket) et `/health` vers l'API, pose les en-têtes de sécurité (CSP, HSTS derrière TLS) | `src/frontend/keke-beauty-web/Dockerfile` |
| `api` | ASP.NET Core 10 (monolithe modulaire), SignalR, tâches de fond (rendez-vous, facturation) | `src/backend/Dockerfile` |
| `postgres` | PostgreSQL 16 + PostGIS (un schéma par module) | `postgis/postgis:16-3.4` ou service managé avec PostGIS |
| `minio` | Stockage objet S3 (`public-media`, `private-kyc`) | MinIO ou tout service compatible S3 |

L'API n'est jamais exposée directement : seul `web` reçoit le trafic public, derrière un terminateur TLS
(load balancer, Caddy, Traefik…) qui transmet `X-Forwarded-Proto`.

## 2. Variables d'environnement de l'API

Notation ASP.NET : `Section__Cle` (double souligné). **Aucun secret dans le dépôt** : utilisez un coffre ou les secrets de la plateforme.

| Variable | Obligatoire | Description |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | oui | `Production` (désactive les envois e-mail en clair dans les logs, refuse la passerelle de paiement `Fake`) |
| `ConnectionStrings__Default` | oui | Chaîne Npgsql (utilisateur dédié, mot de passe fort) |
| `Jwt__SigningKey` | oui | Clé **aléatoire** de 32 caractères minimum, générée par exemple avec `openssl rand -base64 48` et gardée dans le coffre de secrets. **En production l'API refuse de démarrer** si elle est absente, courte, factice ou si c'est une clé publiée dans le dépôt (public). Changer la clé déconnecte tout le monde. En développement, vide = clé aléatoire propre au processus (`JWT_SIGNING_KEY` dans `.env` pour en fixer une) |
| `Auth__WebBaseUrl` | oui | URL publique du site (liens des e-mails), ex. `https://keke-beauty.ci` |
| `Cors__Origins__0` | si web et API sur des origines différentes | Origine autorisée. Inutile avec le proxy nginx fourni (même origine) |
| `AllowedHosts` | recommandé | Nom d'hôte public, ex. `keke-beauty.ci` (défaut `*`) |
| `Proxy__TrustForwardedHeaders` | oui derrière nginx | `true` : l'API lit l'IP réelle (`X-Forwarded-For`) pour la limitation de débit. **Ne jamais l'activer si l'API est joignable directement** |
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
- `MAP_TILES_URL` et `MAP_TILES_ATTRIBUTION` : fournisseur de tuiles de carte. Vide = tuiles CARTO Voyager (données OpenStreetMap ; gratuit pour le développement et un usage non commercial, sans garantie de disponibilité ; les serveurs bénévoles d'OpenStreetMap répondent 504 à de nombreux navigateurs). En production, utilisez un fournisseur avec engagement de service (MapTiler, Stadia Maps, Jawg…) : gabarit `https://…/{z}/{x}/{y}…`, par exemple `https://api.maptiler.com/maps/streets-v2/{z}/{x}/{y}.png?key=VOTRE_CLE` ; restreignez la clé à votre domaine (elle est visible dans le navigateur). L'hôte des tuiles est ajouté automatiquement à la politique CSP (`img-src`).

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
- Les **clés de chiffrement** (Data Protection : secrets TOTP) sont dans la base : la sauvegarde de la base les contient. Sans elles, les secrets 2FA deviennent illisibles.
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
- **Le dépôt est public** : aucun secret n'y a sa place (jetons de paiement, clé JWT, mots de passe réels). Ce qui y figure (les anciens mots de passe de la pile locale `keke_dev`, `keke_minio_dev`, désormais remplacés par des valeurs aléatoires générées par `scripts/init-env.sh` dans `.env`) est connu de tout le monde : la pile de développement ne doit **jamais** être exposée (tunnel, réseau partagé, serveur) ; ses ports ne sont publiés que sur `127.0.0.1`. L'ancienne clé JWT de développement est **brûlée** (présente dans l'historique git) : elle est refusée en production.
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

## 9. LeTexto : SMS et e-mails

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

La sauvegarde contient les clés de chiffrement (Data Protection) : conservez-la comme un secret. Les fichiers des buckets (photos, vidéos, pièces d'identité) ne sont **pas** dans le dump : sauvegardez-les séparément (§4).

## 11. Plusieurs serveurs : Redis pour le temps réel

Avec plusieurs instances de l'API derrière un répartiteur de charge, SignalR relaie ses messages (agenda, créneaux, notifications) par Redis. Renseignez `Redis__ConnectionString` (ex. `redis:6379`, ou `host:6380,ssl=true,password=…`) sur **toutes** les instances ; le service `redis` de `docker-compose.yml` sert pour le développement. Les WebSockets doivent traverser le répartiteur (le `nginx.conf` fourni gère `/hubs/`).

## 12. Traces et métriques (OpenTelemetry)

Désactivé par défaut. Renseignez `OpenTelemetry__OtlpEndpoint` (collecteur OTLP gRPC, ex. `http://otel-collector:4317` ; `OpenTelemetry__Protocol=http` pour OTLP/HTTP) :

- **Traces** : requêtes ASP.NET Core, appels HTTP sortants (paiements, LeTexto, push), requêtes Npgsql, et les spans métier (`booking.create`). Les sondes `/health` et les connexions SignalR sont filtrées.
- **Métriques** : ASP.NET Core, HTTP client, runtime .NET, Npgsql et les compteurs `keke.bookings.created` et `keke.appointment_payments` (étiquette `outcome`).
- En local : `docker compose --profile observability up -d`, `OTEL_EXPORTER_OTLP_ENDPOINT=http://jaeger:4317` dans `.env`, relancer l'API, puis ouvrir http://localhost:16686 (Jaeger).
