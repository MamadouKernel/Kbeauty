# Plan: Lot 3 — Partenaire et KYC

## Backend
- **SharedKernel** : `IFileStorage` (Put/Open/Delete par bucket+clé).
- **Infrastructure** : `S3FileStorage` (AWSSDK.S3 → MinIO, path-style), `ImageProcessor` (ImageSharp : détection du format réel, bornes, WebP), services transverses dans `Partner/` (`PartnerEstablishmentService`, `PartnerCatalogService`, `PartnerMediaService`, `KycService`, `ReviewService`) — seul projet voyant tous les schémas, comme `Search/`.
- **Onboarding** : `KycIdentity`, `EstablishmentReview` (+ configurations, schéma `onboarding`). **Administration** : `AuditLog` (schéma `admin`).
- **Catalog/Scheduling** : entités rendues modifiables (`set`), aucune nouvelle table.
- **Api** : `/api/v1/pro/**` (policy `Partner`), `/api/v1/admin/kyc/**` (policy `Admin`), `/api/v1/files/{**key}` public (cache immutable). Migration `AddOnboarding`.
- Tests : unitaires (slug, contrôles KYC, validation horaires), intégration (parcours complet, autorisations, fichiers invalides) avec stockage en mémoire.

## Frontend
- `/pro` (mes établissements + statut), `/pro/etablissements/:id` (assistant PrimeNG Stepper), `/pro/etablissements/:id/prestations`, `/pro/etablissements/:id/galerie`.
- `/admin/kyc` (file) et `/admin/kyc/:id` (dossier + visionneuse + décision).
- Lien « Inscrire mon établissement » (en-tête/accueil), sélecteur de position Leaflet à pointeur déplaçable.
- Client TypeScript régénéré (`npm run api:types`).

## Risques
- Licence ImageSharp (Six Labors Split License : gratuite pour l'open source et < 1 M$ de revenu) — à revoir avant commercialisation.
- Nom/numéro de pièce stockés en clair en base (chiffrement applicatif à envisager) ; bucket privé sans chiffrement serveur en dev (SSE à activer en production).
