# Phase 0 — Research: Inscription et Authentification Client par Téléphone

## Décision 1 — Intégration Zavu via appel HTTP direct (pas de SDK)

- **Decision**: `ZavuWhatsAppOtpSender` appelle l'API REST Zavu via un `HttpClient` nommé, configuré
  avec l'URL de base (`https://api.zavu.dev`) et la clé API lue depuis la configuration
  (`Zavu:ApiKey`, jamais commitée), plutôt que d'installer un SDK .NET dédié.
- **Rationale**: Aucun SDK .NET officiel Zavu n'est connu à ce stade ; le CLI `zavudev` (Node.js) et
  les skills `zavu-skills:*` installés sont des outils d'agent/CLI, pas une bibliothèque appelable
  depuis le code C#. Un appel HTTP direct reste la voie la plus simple et la plus stable.
- **Alternatives envisagées**: Invoquer le CLI `zavudev` en sous-processus depuis l'API .NET — rejeté
  (fragile, dépendance Node.js dans le conteneur .NET, latence, pas idiomatique) ; attendre un SDK
  officiel — rejeté (bloquerait indéfiniment la feature).

## Décision 2 — Table technique dédiée `otp_challenge` plutôt que colonnes sur `utilisateur`

- **Decision**: Les codes OTP sont stockés dans une nouvelle table `otp_challenge`, indépendante de
  `utilisateur`, plutôt que d'ajouter des colonnes `otp_code`/`otp_expires_at` sur `utilisateur`.
- **Rationale**: Un OTP peut être demandé avant même qu'un compte `utilisateur` existe (première
  inscription) — il ne peut donc pas être une colonne d'une ligne `utilisateur` qui n'existe pas
  encore. Une table séparée permet aussi de conserver un historique des tentatives sans polluer
  l'entité métier `utilisateur` déjà validée en MERISE (Principe II — ne pas redéfinir une entité
  MERISE existante pour un besoin technique).
- **Alternatives envisagées**: Colonnes sur `utilisateur` — rejeté pour la raison ci-dessus.

## Décision 3 — Hachage du code OTP en base (FR-010)

- **Decision**: Seul un hash SHA-256 du code (avec un sel/pepper applicatif) est stocké dans
  `otp_challenge.code_hash` ; le code en clair n'existe qu'en mémoire le temps de l'envoi et de la
  comparaison, jamais journalisé.
- **Rationale**: Répond directement à FR-010 (jamais de code en clair accessible autrement que par
  l'envoi au numéro concerné). Un hash simple suffit ici car le code est court-vécu (5 min) et à usage
  unique — pas besoin d'un algorithme de hachage de mot de passe coûteux (bcrypt) pour cet usage.
- **Alternatives envisagées**: Stockage en clair (rejeté — viole FR-010 et le Principe V de la
  constitution) ; bcrypt (rejeté comme surdimensionné pour un code à courte durée de vie, coût CPU
  inutile à ce volume).

## Décision 4 — Invalidation des codes précédents (FR-006)

- **Decision**: À la génération d'un nouveau code pour un numéro+type de compte donné, tous les
  challenges `PENDING` existants pour ce couple sont marqués `INVALIDATED` dans la même transaction
  que la création du nouveau challenge.
- **Rationale**: Garantit qu'un seul code reste valide à la fois par numéro (FR-006), sans dépendre
  d'une tâche de nettoyage asynchrone.
- **Alternatives envisagées**: Suppression physique des anciens challenges — rejeté, un statut
  `INVALIDATED` conserve une trace utile pour audit/diagnostic sans risque de sécurité (le hash seul
  ne permet pas de retrouver le code).

## Décision 5 — Échec explicite si Zavu n'est pas configuré (Edge Case / FR-009)

- **Decision**: Si la clé API Zavu est absente de la configuration, `ZavuWhatsAppOtpSender` retourne
  immédiatement un échec explicite (pas d'exception non gérée), sans tenter d'appel réseau. Si la clé
  est présente mais l'appel échoue (réseau, erreur Zavu), l'échec est également explicite.
- **Rationale**: Cohérent avec le choix utilisateur (Option A : implémentation Zavu réelle, différée
  jusqu'à configuration complète) et avec FR-009 — le comportement en attendant la configuration Zavu
  est un échec propre et testable, pas un blocage silencieux ni un succès simulé.
- **Alternatives envisagées**: Sender factice retournant toujours succès en l'absence de config —
  rejeté explicitement par l'utilisateur (Option B refusée au profit de l'Option A).

Toutes les inconnues techniques sont résolues ; prêt pour la Phase 1 (Design & Contracts).
