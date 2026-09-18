# Sprint 9 — Frontend client, annuaire et RDV (Keke Beauty)

**Objectif** : premier frontend web (Blazor), parcours client — recherche annuaire, fiche
établissement, authentification OTP, prise de RDV avec créneaux occupés visibles.

## Sprint Backlog
| Story | Statut |
|---|---|
| Recherche et fiche établissement | Fait (testé en réel dans le navigateur : filtres, liste, fiche, `tel:`, itinéraire) |
| Authentification OTP client | Fait (testé en réel : échec explicite affiché sans Zavu configuré) |
| Prise de RDV | Fait (redirection vers `/login` si non authentifié testée en réel ; création RDV, créneaux occupés et `GET /rdv/{id}` validés via curl — login UI bloqué par Zavu comme le reste du projet) |

## Definition of Done
- [x] Recherche/fiche établissement fonctionnelles, testées en réel dans le navigateur.
- [x] Échec explicite (jamais d'écran vide) sur panne API ou OTP non configuré, testé en réel.
- [x] Redirection vers `/login` pour un client non authentifié, testée en réel.
- [x] Nouveau `GET /rdv/{id}` (ajout backend minimal) testé en réel via curl, cohérent avec le
  format consommé par le frontend.
- [x] `docker compose up -d --build api web` opérationnel, port dédié `WEB_PORT`.

## Dette technique explicite (confirmée)
- Session client en `ProtectedLocalStorage` (pas de JWT), cohérent avec `X-Client-Id` côté API.
- Sélection de créneau/heure manuelle (pas de calendrier visuel graphique) — amélioration UX
  différée à une itération de raffinement du design (voir spec.md Assumptions).
- Parcours partenaire et admin non couverts par ce sprint (frontend dédié à prévoir).

## Prochaine étape
Itérations frontend suivantes : parcours partenaire (gestion établissement/prestations/RDV/
abonnement) et parcours admin (KYC, modération, tarifs abonnement), puis raffinement du design et
base mobile (.NET MAUI Blazor Hybrid réutilisant les mêmes composants Razor).
