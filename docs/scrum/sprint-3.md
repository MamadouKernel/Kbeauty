# Sprint 3 — Inscription et Validation KYC Partenaire (Keke Beauty)

**Objectif de sprint** : permettre à un gérant de soumettre un dossier partenaire (compte + établissement
+ pièces KYC) et à un administrateur de le valider ou le rejeter.

## Sprint Backlog

| Story | Tâches | Statut |
|---|---|---|
| US-04 — Soumission dossier | `POST /partners/applications` (multipart), stockage local des fichiers | Fait |
| US-04 — Référentiel géo minimal | Migration `0004_geo_seed_minimal.sql` (dépendance découverte) | Fait |
| US-05 — Consultation admin | `GET /admin/applications`, `GET /admin/applications/{id}`, `GET .../files/{type}` | Fait |
| US-05 — Décision admin | `POST /admin/applications/{id}/validate`, `.../reject` | Fait |
| US-05 — Protection admin | Clé d'API statique temporaire (`ADMIN_API_KEY`) | Fait (dette technique documentée) |
| US-05 — Notification rejet | `ZavuWhatsAppPartnerNotifier` (dépend de Zavu, comme US-03) | Fait (échec explicite en attendant Zavu) |

## Definition of Done du Sprint 3
- [x] Soumission complète → `201 EN_ATTENTE`, fichiers écrits sur le volume, ID cohérent avec la base (testé en réel).
- [x] Soumission incomplète → `400 invalid_submission` explicite (testé en réel).
- [x] Liste, détail, téléchargement de fichier admin fonctionnels (testé en réel).
- [x] Validation → `200 VALIDE` (testé en réel).
- [x] Rejet → `200 REJETE` + tentative de notification (échec explicite sans Zavu, testé en réel).
- [x] Accès admin sans clé → `401` (testé en réel).
- [x] Statut `EN_ATTENTE`/`VALIDE`/`REJETE` vérifié directement en base (testé en réel).

## Bugs corrigés pendant l'implémentation
1. **ID de fichier incohérent** : l'établissement était créé avec un ID généré par la base
   (`gen_random_uuid()` par défaut), différent de celui utilisé pour nommer les fichiers stockés.
   Corrigé en générant l'ID côté application et en l'insérant explicitement.
2. **Mapping Dapper de records positionnels** (`ApplicationSummary`, `ApplicationDetail`) : même
   symptôme que dans la feature 003 (`InvalidOperationException` au runtime). Corrigé en utilisant des
   classes à propriétés mutables plutôt que des `record` positionnels pour tout type matérialisé
   directement par Dapper.
3. **Risque d'injection SQL** repéré en revue : le type de fichier (`devanture`/`piece-identite`),
   fourni via un paramètre de route utilisateur, était interpolé dans une requête SQL. Corrigé en le
   passant comme paramètre lié dans une expression `CASE` fixe.

## Dette technique explicite
- Protection admin par clé statique — à remplacer par une vraie authentification/RBAC administrateur.
- Référentiel géographique limité à une seule commune (seed minimal) — à remplacer par la future
  feature back-office référentiels (US-10).

## Prochaine étape
`005-recherche-annuaire` (US-06 à US-09) DOIT filtrer les résultats sur `statut_kyc = 'VALIDE'`
(dépendance identifiée dans `specs/004-onboarding-partenaire/tasks.md`, T025) — à ne pas oublier lors
de sa spécification.
