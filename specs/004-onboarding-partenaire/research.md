# Phase 0 — Research: Inscription et Validation KYC des Établissements Partenaires

## Décision 1 — Stockage fichier : volume Docker local, pas de service cloud

- **Decision**: Les fichiers (photo devanture, pièce d'identité) sont écrits sur un volume Docker
  nommé, monté dans le conteneur `kekebeauty-api` à `/app/storage/kyc/{idEtablissement}/`. Les colonnes
  `url_photo_devanture`/`url_piece_identite` stockent un chemin relatif (`kyc/{id}/devanture.jpg`).
- **Rationale**: Décision confirmée par l'utilisateur (upload direct). Aucun compte de service cloud
  (S3, Azure Blob) n'existe encore pour ce projet ; un volume local suffit pour démarrer et respecte
  YAGNI. Le chemin relatif stocké en base (plutôt qu'une URL absolue) permet de changer de backend de
  stockage plus tard sans migration de données.
- **Alternatives envisagées**: Stockage cloud (S3-compatible) — rejeté pour l'instant (nouveau compte
  tiers à créer, disproportionné pour une première itération) ; stockage en base (BYTEA) — rejeté
  (dégrade les performances de lecture/écriture de la table `etablissement`).

## Décision 2 — Accès aux fichiers via endpoint protégé, jamais via chemin statique public

- **Decision**: Les fichiers ne sont jamais servis par un dossier statique public ASP.NET Core.
  L'unique accès en lecture passe par `GET /admin/applications/{id}/files/{type}`, qui vérifie la clé
  d'accès admin puis stream le fichier depuis le disque.
- **Rationale**: Répond directement à FR-011 — aucune URL publique ne doit jamais permettre à un
  client ou un partenaire d'accéder à la pièce d'identité d'un autre gérant.
- **Alternatives envisagées**: `app.UseStaticFiles()` sur le dossier de stockage — rejeté explicitement
  (violerait FR-011, aucun contrôle d'accès possible sur des fichiers statiques nommés prévisibles).

## Décision 3 — Référentiel géographique minimal (dépendance découverte)

- **Decision**: Une migration `0004_geo_seed_minimal.sql` insère un jeu minimal de données
  (1 pays, 1 région, 1 ville, 1 commune — ex. Côte d'Ivoire > Abidjan > Abidjan > Cocody) pour permettre
  la création d'un établissement, la contrainte `etablissement.id_commune NOT NULL` l'exigeant.
- **Rationale**: La feature US-10 (back-office référentiels géographiques) n'existe pas encore dans le
  Product Backlog implémenté ; sans données géographiques, aucun établissement ne peut être créé, ce
  qui bloquerait entièrement cette feature. Un seed minimal permet d'avancer sans anticiper toute la
  gestion CRUD du référentiel (hors périmètre de cette feature).
- **Alternatives envisagées**: Rendre `id_commune` nullable — rejeté (contraire au MPD validé,
  Principe II non-négociable) ; développer maintenant toute la gestion CRUD des zones géographiques —
  rejeté (hors périmètre de cette feature, disproportionné, violerait YAGNI).

## Décision 4 — Protection minimale des endpoints admin par clé d'API statique

- **Decision**: Les endpoints `/admin/applications/*` exigent un header `X-Admin-Api-Key` comparé à
  `Admin:ApiKey` (configuration, jamais commitée). Réponse `401` explicite si absent/incorrect.
- **Rationale**: Aucune feature d'authentification administrateur n'est encore développée. Exposer ces
  endpoints sans aucun contrôle violerait le Principe V de la constitution et FR-011. Une clé statique
  est le contrôle minimal proportionné (YAGNI) en attendant une vraie feature d'authentification/RBAC
  administrateur.
- **Alternatives envisagées**: Aucune protection — rejeté (violation de sécurité directe) ; construire
  une authentification complète maintenant — rejeté comme disproportionné pour cette seule feature
  (voir Complexity Tracking du plan, qui documente ce compromis comme dette technique explicite).

## Décision 5 — Notification de rejet : nouvel notifier dédié, pas de réutilisation de `IOtpSender`

- **Decision**: Un nouveau `IPartnerNotifier` (implémentation `ZavuWhatsAppPartnerNotifier`) envoie un
  message texte libre au gérant, distinct de `IOtpSender` (dédié aux codes OTP de la feature 003).
- **Rationale**: `IOtpSender` a une signature spécifique à l'envoi d'un code ; forcer sa réutilisation
  pour un message de notification générique couplerait deux responsabilités différentes. Le risque de
  duplication (appel HTTP Zavu similaire) est accepté pour ne pas toucher au code déjà livré et testé
  de la feature 003 (stabilité > DRY strict ici).
- **Alternatives envisagées**: Étendre `IOtpSender` avec une méthode générique — rejeté (élargirait une
  interface déjà testée et livrée, risque de régression sur 003 pour un bénéfice marginal).

Toutes les inconnues techniques sont résolues ; prêt pour la Phase 1 (Design & Contracts).
