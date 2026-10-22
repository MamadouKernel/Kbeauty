# Revue interne ASVS 4.0.3, niveau 2

Date : 20 octobre 2026 · Périmètre : l'application (API .NET, site Angular, configuration de la pile Docker) · Auteur : équipe de
développement. **Ce n'est pas une certification ni un audit indépendant** : chaque ligne indique la preuve disponible (test, scan,
code) ; ce qui n'a pas pu être vérifié est dit.

Légende : ✅ conforme, preuve à l'appui · 🟡 partiel ou à compléter · ❌ écart connu · ➖ hors périmètre du dépôt (à traiter à l'exploitation).

## V1 Architecture, conception, modèle de menaces

| Exigence | État | Preuve ou écart |
|---|---|---|
| Composants et frontières de confiance identifiés (modules, API, stockage, fournisseurs) | ✅ | `docs/DEPLOYMENT.md` §1, modules séparés par schéma |
| Contrôles de sécurité appliqués côté serveur, refus par défaut | ✅ | `FallbackPolicy` : une route non déclarée répond 401 à un anonyme ; autorisation par rôle **et** propriété |
| Modèle de menaces formel (STRIDE) documenté | ❌ | Les tournées `docs/PENTEST.md` tiennent lieu d'analyse ; pas de modèle formel |

## V2 Authentification

| Exigence | État | Preuve ou écart |
|---|---|---|
| Mot de passe : 10 à 128 caractères, pas de règle de composition excessive | ✅ | `PasswordRules` ; tests unitaires |
| Refus des mots de passe courants (2.1.7) | 🟡 | Liste de mots et motifs courants avec substitutions (`P@ssw0rd`), testée ; **pas** de vérification contre les mots de passe déjà fuités (service de type « k-anonymat ») : à ajouter |
| Stockage : fonction de dérivation lente, sel, rehachage | ✅ | PBKDF2-HMAC-SHA512, **210 000 itérations** (test sur le vrai hacheur), mise à niveau automatique à la connexion |
| Pas d'énumération de comptes (connexion, inscription, mot de passe oublié) | ✅ | 4 sondes : même statut et même message |
| Second facteur résistant à la force brute | ✅ | TOTP obligatoire pour partenaires et admins ; **verrouillage par compte** après 5 mauvais codes, doublé à chaque récidive (15 min à 24 h), tests unitaires et d'intégration ; rejeu d'un code refusé |
| Codes de récupération à usage unique, hachés | ✅ | `AuthFlowTests` |
| Réinitialisation : jeton à usage unique, lien non empoisonné par l'en-tête `Host` | ✅ | sonde dédiée, test d'intégration |
| Second facteur résistant à l'hameçonnage (WebAuthn / passkeys) | ❌ | TOTP seulement : proposer les passkeys aux partenaires et admins |

## V3 Gestion des sessions

| Exigence | État | Preuve ou écart |
|---|---|---|
| Jetons d'accès courts (15 min), renouvellement par cookie `HttpOnly` + `SameSite` | ✅ | `AuthOptions`, sonde cookies |
| Renouvellement avec détection de rejeu (un jeton déjà utilisé met fin à toutes les sessions) | ✅ | `AuthService` (lecture du code) et tests |
| Révocation immédiate (suspension, déconnexion, retrait de rôle) | ✅ | Compte relu en base à chaque requête (tournée 2) |
| Cookie `Secure` | ➖ | Posé dès que la requête arrive en HTTPS : à valider derrière le TLS de production |

## V4 Contrôle d'accès

| Exigence | État | Preuve ou écart |
|---|---|---|
| Refus par défaut, rôles et propriété vérifiés côté serveur | ✅ | 14 routes inter-établissements testées (404), `FallbackPolicy` |
| Pas de références directes d'objet exploitables (IDOR) | ✅ | Tests croisés partenaire A/B, client/admin ; l'existence n'est jamais révélée |
| Séparation des fonctions d'administration | ✅ | Politiques `SuperAdmin`, `AdminFinance`, `AdminSupport` ; sauvegardes réservées au SuperAdmin |

## V5 Validation, assainissement et encodage

| Exigence | État | Preuve ou écart |
|---|---|---|
| Requêtes paramétrées (pas d'injection SQL) | ✅ | EF Core ; sondes d'injection, ZAP |
| Entrées malformées : jamais de 5xx | ✅ | JSON invalide, octet NUL, dates et identifiants invalides, pagination hors bornes (sondes) |
| Taille des requêtes bornée | ✅ | 256 Ko par défaut côté API, 1 Mo côté nginx ; 4 routes d'envoi avec leur plafond ; test de non-régression |
| Encodage en sortie / XSS | ✅ | Angular (échappement automatique), CSP stricte sans `unsafe-inline` pour les scripts, ZAP |
| Export CSV : injection de formules | ✅ | Neutralisation testée (tournée 2) |

## V6 Cryptographie

| Exigence | État | Preuve ou écart |
|---|---|---|
| Algorithmes éprouvés, aléa cryptographique | ✅ | AES-256 (Data Protection), HMAC-SHA256 (rappels), `RandomNumberGenerator` |
| Clés hors du code et de la base | ✅ | Secrets en `.env` ou fichiers montés, jamais versionnés (gitleaks, hook, CI) |
| Secrets TOTP chiffrés, clés de chiffrement chiffrées à leur tour | ✅ | Anneau de clés chiffré AES-256-GCM par une clé du keystore, obligatoire en production, conversion automatique, tests |
| Rotation des clés (jetons, clé de chiffrement) | ❌ | Procédure non écrite ni outillée : à définir avant l'ouverture |

## V7 Erreurs et journalisation

| Exigence | État | Preuve ou écart |
|---|---|---|
| Pas de détail technique dans les erreurs | ✅ | Réponses `problem+json` génériques ; ZAP « Application Error Disclosure » : réussi |
| Pas de donnée sensible dans les journaux | ✅ | Chaîne de requête des WebSocket exclue des journaux nginx (jeton) ; secrets jamais journalisés |
| Journal d'audit des actions d'administration et des accès aux pièces d'identité | ✅ | `admin.audit_logs` |
| Alertes sur les événements de sécurité | 🟡 | 15 règles (disponibilité, 5xx, paiements, rappels falsifiés, rafale de connexions, verrouillage du second facteur, quota et webhook WhatsApp) testées par un exercice réel ; à compléter par les destinataires d'astreinte de production |

## V8 Protection des données

| Exigence | État | Preuve ou écart |
|---|---|---|
| Pièces d'identité : stockage privé, accès journalisé | ✅ | Bucket `private-kyc`, jamais public |
| Pas de mise en cache de données sensibles | ✅ | `Cache-Control: no-store` sur l'API |
| Droits des personnes (export, effacement) | ✅ | « Mon compte », testé |
| Chiffrement au repos du stockage et de la base | ➖ | À activer chez l'hébergeur (chiffrement de volume, SSE du stockage S3) |

## V9 Communications

| Exigence | État | Preuve ou écart |
|---|---|---|
| TLS 1.2+ partout, HSTS | ➖ | HSTS posé dès que la requête est en HTTPS ; le TLS est fourni devant le conteneur `web` : à valider (SSL Labs) sur l'environnement final |
| Pas de contenu mixte, origines tierces limitées | ✅ | CSP restreinte à l'origine du site ; tuiles de carte servies par le site |

## V10 Code malveillant

| Exigence | État | Preuve ou écart |
|---|---|---|
| Dépendances sans vulnérabilité connue | ✅ | NuGet et npm : 0 ; vérifié aussi **à chaque build** (CI) |
| Mise à jour continue des dépendances et images | ✅ | Dependabot (paquets, images, actions) |
| Secrets absents du code et de l'historique récent | ✅ | gitleaks (fichiers + historique) ; hook de pré-commit ; CI |
| Nomenclature logicielle (SBOM) et images épinglées par empreinte | ❌ | Non produite ; images épinglées par version, pas par empreinte |

## V11 Logique métier

| Exigence | État | Preuve ou écart |
|---|---|---|
| Limites de débit sur les actions sensibles | ✅ | Connexion, 2FA, réservation, cartes cadeaux, relecture de paiement (par utilisateur) |
| Paiements : montant, devise, idempotence, rejeu | ✅ | Tests d'intégration ; rappel signé ; relecture serveur |

## V12 Fichiers

| Exigence | État | Preuve ou écart |
|---|---|---|
| Type détecté par le **contenu**, ré-encodage, taille bornée, bombes de décompression | ✅ | `ImageProcessor` (WebP, EXIF et contenu ajouté supprimés) ; envoi réel de 4,3 Mo testé, 18 Mo refusé |
| Pas de traversée de chemin | ✅ | 4 sondes |

## V13 API

| Exigence | État | Preuve ou écart |
|---|---|---|
| Authentification et autorisation sur chaque route, types de contenu vérifiés | ✅ | ZAP api-scan actif (anonyme, client, partenaire, admin), `FallbackPolicy` |
| CORS fermé aux origines tierces | ✅ | Sonde |
| Documentation de l'API non exposée en production | ✅ | N'existe qu'en mode développement |

## V14 Configuration

| Exigence | État | Preuve ou écart |
|---|---|---|
| En-têtes de sécurité (CSP, `nosniff`, `X-Frame-Options`, COOP, CORP) | ✅ | Sondes, ZAP |
| Pas de secret par défaut ; démarrage refusé en production si la configuration est dangereuse | ✅ | 4 refus vérifiés (clé de chiffrement absente, clé JWT publiée, passerelle simulée, clé invalide) |
| Conteneurs sans privilèges, images maintenues et scannées | ✅ | API et nginx sans root ; Trivy : voir `docs/PENTEST.md` (cinquième et sixième tournées) |
| Pile de développement non exposée | ✅ | Ports sur `127.0.0.1` uniquement |

## Écarts à traiter, par priorité

1. **Rotation des clés** (jetons de session, clé de chiffrement du keystore) : écrire et tester la procédure (V6).
2. **Destinataires des alertes en production** (V7) : les règles existent et ont été éprouvées ; il reste à brancher l'astreinte réelle.
3. **Passkeys / WebAuthn** pour partenaires et admins (V2) ; **vérification des mots de passe fuités** (V2).
4. **TLS, chiffrement au repos, SBOM, images épinglées par empreinte** à la mise en production (V8, V9, V10).
5. **Audit indépendant** : cette revue le prépare mais ne le remplace pas.
