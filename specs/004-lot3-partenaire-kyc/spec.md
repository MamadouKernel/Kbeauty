# Spec: Lot 3 — Partenaire et KYC

Source : PRD §7.5 (ONB-01 à 04), §7.6 (SVC-01 à 03), §7.10 (ADM-03), §8.2, §9, §10.4.
Objectif du lot : un partenaire s'inscrit, un admin valide, la fiche apparaît dans l'annuaire.

## Adaptations par rapport au PRD (décisions de ce projet)
- L'authentification est **e-mail + mot de passe + Google Authenticator** (lot 1), pas l'OTP SMS : « je me connecte » = compte `PartnerOwner`.
- Les téléversements passent **par l'API** (multipart), pas par URL pré-signée : le serveur valide le type réel, la taille et ré-encode l'image avant de la stocker (pas de fichier non vérifié dans le bucket, EXIF retiré).
- Les pièces d'identité sont **servies par l'API aux seuls admins** (flux direct, `Cache-Control: no-store`, journal d'audit) au lieu d'une URL pré-signée de 5 min : même garantie, sans exposer MinIO.
- **Photos uniquement** dans ce lot ; les vidéos (flag de plan `videos`, durée, poster) arrivent avec les abonnements (lot 6). Limite de photos configurable (`Media:MaxPhotos`, 5 par défaut = plan Gratuit).
- Notifications : e-mail journalisé (`IEmailSender`) au partenaire à chaque décision ; SMS et in-app au lot 5.

## Parcours
1. **Inscription** avec le rôle partenaire (déjà disponible), connexion + 2FA.
2. **Assistant** (« Inscrire mon établissement ») : (1) informations de base, (2) localisation (commune + adresse + position GPS déplaçable), (3) horaires, (4) photo de devanture, (5) pièce d'identité du gérant, (6) récapitulatif et soumission. Brouillon enregistré à chaque étape, reprise possible.
3. **Soumission** → établissement `PendingReview`, dossier dans la file admin. Invisible dans l'annuaire tant que non `Approved`.
4. **Admin** : file (plus anciens d'abord), visionneuse des pièces, contrôles indicatifs (document expiré, nom différent, doublon de numéro), décision Approuver / Rejeter (motif obligatoire) / Demander des corrections (commentaire obligatoire).
5. **Après approbation** : fiche `Active` dans l'annuaire ; le partenaire gère prestations, rubriques et galerie (couverture, ordre, suppression).
6. **Multi-établissements** : un gérant crée plusieurs établissements ; la pièce d'identité approuvée est réutilisée.

## Règles
- Statuts KYC identité : `Draft → PendingReview → Approved | Rejected | ChangesRequested`. Établissement : `Draft → PendingReview → Active | Rejected` ; « corrections demandées » ramène l'établissement en `Draft` avec le commentaire.
- Modification permise en `Draft` et `Active` ; verrouillée en `PendingReview`, `Rejected`, `Suspended`.
- Soumission refusée (409 + liste `missing`) tant que : nom, catégorie, description, téléphone, commune, adresse, position, ≥ 1 plage horaire, ≥ 1 photo, pièce d'identité complète (ou déjà approuvée).
- Un partenaire n'accède qu'à ses établissements (404 sinon). `/api/v1/admin/**` réservé à `Admin`/`SuperAdmin`.
- Pièces : bucket privé `private-kyc`, jamais d'URL publique ; chaque consultation est inscrite dans `audit_logs`. Photos publiques : bucket `public-media`, servies via `/api/v1/files/{clé}`.
- Photos : JPEG/PNG/WebP, 10 Mo max, dimensions bornées, ré-encodées en WebP (taille moyenne 1600 px + miniature 400 px). Pièces : mêmes formats, 10 Mo, ré-encodées (2400 px max).

## Critères d'acceptation
- Un partenaire complète le parcours et soumet ; l'admin approuve ; la fiche apparaît dans `GET /establishments` et par slug.
- Rejet/corrections : la fiche reste invisible, le partenaire voit le motif et peut corriger puis resoumettre (corrections).
- Un client ne peut pas appeler `/pro/**` ; un partenaire ne peut pas appeler `/admin/**` ni lire l'établissement d'un autre.
- Fichiers invalides (faux type MIME, trop gros, corrompus) refusés en 400/413.
