# Sprint 4 — Recherche et Consultation de l'Annuaire Client (Keke Beauty)

**Objectif de sprint** : permettre à un client de rechercher des établissements validés par
catégorie/commune et de consulter leur fiche complète (médias, prestations, contact, itinéraire).

## Sprint Backlog

| Story | Tâches | Statut |
|---|---|---|
| US-06 — Recherche | `GET /etablissements?categorie=&commune=`, INNER JOIN garantissant FR-002 | Fait |
| US-07/US-08 — Fiche | `GET /etablissements/{id}` (médias, prestations, numéro de service client) | Fait |
| US-09 — Itinéraire | Lien Google Maps généré depuis les coordonnées GPS | Fait |
| US-04/US-05 (complément) — Données de test | Assignation catégorie/prestation via endpoints admin minimaux (FR-008) | Fait (dette technique documentée) |

## Definition of Done du Sprint 4
- [x] Recherche par catégorie → établissement validé et catégorisé trouvé (testé en réel).
- [x] Recherche sans résultat → liste vide, pas d'erreur (testé en réel).
- [x] Établissement `REJETE` avec catégorie assignée → absent de la recherche (testé en réel).
- [x] Fiche complète (nom, numéro, lien Google Maps, prestations) → conforme (testé en réel).
- [x] Fiche d'un établissement `REJETE` et d'un ID inexistant → même `404` (testé en réel, pas de fuite).

## Dette technique explicite (héritée et confirmée)
- Endpoints d'assignation catégorie/prestation (FR-008) : mécanisme temporaire, protégé par la même
  clé admin statique que la feature 004 — à remplacer par une vraie gestion partenaire (Epic
  "Établissement", non encore planifiée dans le backlog).
- Recherche sans pagination/tri/texte libre — hors périmètre (spec, Assumptions).

## Bug corrigé pendant l'implémentation
Collision de nom d'espace de noms : `KekeBeauty.Infrastructure.Directory` masquait `System.IO.Directory`
utilisé par `LocalFileStorage` (feature 004), provoquant une erreur de compilation. Renommé en
`KekeBeauty.Infrastructure.Listing`.

## Prochaine étape
Le Product Backlog restant porte sur la prise de RDV (Epic 4, US-11 à US-14) et l'abonnement/paiement
(Epic 5). La prise de RDV dépendra de cette recherche/fiche pour identifier l'établissement cible.
