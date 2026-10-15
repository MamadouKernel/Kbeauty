# Tasks: Lot 3 — Partenaire et KYC

## Backend
- [x] T301 `IFileStorage` (SharedKernel) + `S3FileStorage` (MinIO, buckets `public-media` / `private-kyc` créés à la demande) + `InMemoryFileStorage` (tests)
- [x] T302 `ImageProcessor` (SkiaSharp, MIT) : type réel (JPEG/PNG/WebP statiques), 10 Mo, dimensions et pixels bornés, EXIF appliqué puis retiré, ré-encodage WebP (1600 px + miniature 400 px ; pièces 2400 px)
- [x] T303 Onboarding : `KycIdentity`, `EstablishmentReview` ; Administration : `AuditLog` ; migration `AddOnboarding`
- [x] T304 Services partenaire : établissement (création/édition/horaires/soumission, plafond 10 par gérant), prestations et rubriques (suppression logique, ordre), galerie (limite configurable `Media:MaxPhotos`, couverture, ordre), identité (écriture seule)
- [x] T305 Admin : file, détail avec contrôles indicatifs (expiré, nom différent, doublon), lecture d'une pièce (flux privé + audit), décision Approuver / Rejeter / Corrections + e-mail au partenaire
- [x] T306 Endpoints `/api/v1/pro/**` (policy `Partner`), `/api/v1/admin/kyc/**` (policy `Admin`), `/api/v1/files/{**key}` (public, `uploads/` uniquement, cache immutable) ; limite de taille de requête sur les envois
- [x] T307 Tests : 46 unitaires (slug, noms, téléphone, contrôles KYC, URLs média) + 61 d'intégration (parcours complet, autorisations, corrections/rejet, fichiers invalides, galerie, horaires, prestations)

## Frontend
- [x] T311 Services `PartnerService` et `ReviewService`, client TypeScript régénéré
- [x] T312 Espace `/pro` : liste, statuts, éléments manquants, commentaires de l'équipe
- [x] T313 Assistant en 6 étapes (PrimeNG Stepper) : infos, localisation (cascade + carte Leaflet à repère déplaçable + « ma position »), horaires (plusieurs plages/jour), photos, identité, récapitulatif ; reprise à la première étape incomplète
- [x] T314 `/pro/etablissements/:id/prestations` (rubriques, prestations, ordre) et `/galerie` (ajout multiple, couverture, ordre, suppression)
- [x] T315 Admin : `/admin/kyc` (file par statut, plus anciens d'abord) et `/admin/kyc/:id` (dossier, contrôles, visionneuse de pièces, décision)
- [x] T316 Tests Vitest (86 au total) ; parcours réel dans le navigateur (partenaire → soumission → admin → publication)

## Limites connues
- **Vidéos** : au lot 6 avec les plans (flag `videos`, durée, poster). Photos limitées à 5 (`Media:MaxPhotos`) en attendant les plans.
- **Notifications** : e-mail journalisé (pas de SMTP/SMS avant le lot 5) ; pas d'alerte aux admins à la soumission (la file sert de compteur).
- **Pièces d'identité** : servies par l'API aux seuls admins (pas d'URL pré-signée), journal d'audit ; numéro stocké en clair en base ; chiffrement serveur du bucket (SSE) à activer en production ; purge des dossiers refusés après 90 jours non implémentée (lot 6, paramètres).
- **Concurrence** : deux admins décidant simultanément le même dossier ne sont pas sérialisés (dernier écrit gagne) ; collision de slug simultanée possible (index unique, erreur 500 rare).
- **Débit** : pas de limitation de débit dédiée sur `/pro/**` (seule la limite de taille de requête).
- **ImageSharp écarté** : toutes ses versions exigent une licence commerciale pour les builds Release et les versions sans licence ont des CVE ouvertes ; SkiaSharp (MIT) retenu.
