# Research: Frontend client — annuaire et prise de RDV

## Décision 1 — Blazor Web App (Server) plutôt que WebAssembly
**Décision**: render mode Server (interactivité côté serveur, template .NET 8 `blazor`).
**Rationale**: pas de CORS à gérer (le serveur Blazor appelle l'API via réseau Docker interne),
pas de secret/config exposé au navigateur, cohérent avec le choix "web d'abord, mobile via MAUI
Blazor Hybrid ensuite" — les composants Razor restent réutilisables indépendamment du render mode.
**Alternatives rejetées**: Blazor WebAssembly (nécessiterait CORS sur l'API + exposerait l'URL de
l'API au navigateur, complexité inutile pour cette première itération).

## Décision 2 — Session client en ProtectedLocalStorage plutôt que cookie/JWT
**Décision**: `idUtilisateur` obtenu après OTP conservé côté navigateur via
`ProtectedBrowserStorage` (chiffré par clé serveur Blazor Data Protection).
**Rationale**: cohérent avec la dette technique déjà acceptée côté API (`X-Client-Id` porte
directement l'identité, pas de session/JWT) — le frontend ne doit pas inventer un mécanisme de
sécurité plus fort que ce que l'API vérifie réellement.
**Alternatives rejetées**: cookie de session ASP.NET Core classique (donnerait une fausse
impression de sécurité alors que l'API elle-même ne valide qu'un GUID brut).

## Décision 3 — Ajout de `GET /rdv/{id}` plutôt qu'une liste complète
**Décision**: endpoint minimal retournant le statut d'un RDV par son id (déjà connu du frontend
depuis la réponse de création), protégé par vérification que `X-Client-Id` est bien le client
propriétaire.
**Rationale**: suffisant pour US3 AC3 (voir le statut évoluer) sans construire une pagination/liste
complète non demandée par le CDC pour cette itération (YAGNI).
**Alternatives rejetées**: `GET /clients/{id}/rdv` (liste complète) — reporté à une itération
ultérieure si un tableau de bord client est demandé.
