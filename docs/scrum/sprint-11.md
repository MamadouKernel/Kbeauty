# Sprint 11 — Frontend admin, correctif prerendering (Keke Beauty)

**Objectif** : troisième frontend web (Blazor), parcours administrateur — KYC, modération,
abonnements. Aucun ajout backend requis.

## Sprint Backlog
| Story | Statut |
|---|---|
| Connexion admin (clé API) | Fait (testé en réel dans le navigateur) |
| Dossiers KYC (liste/filtre/validation/rejet) | Fait (testé en réel : filtre par statut, dossier visible) |
| Modération (suspension/réactivation par id) | Fait (testé en réel : "Compte introuvable" sur id inexistant) |
| Abonnements (liste/relance/tarifs) | Fait (testé en réel : tarifs affichés, filtre) |

## Bug critique découvert et corrigé
Toutes les pages Blazor Server qui lisent une session (`ProtectedLocalStorage`) dans
`OnInitializedAsync` (client 010, partenaire 011, admin 012) souffraient d'une race de
prerendering : la lecture échoue silencieusement pendant le rendu statique initial (JS interop
indisponible), provoquant une redirection immédiate et invisible vers la page de connexion — même
après une connexion réussie. Corrigé par `@rendermode @(new InteractiveServerRenderMode(prerender: false))`
sur les 8 pages concernées. Ce bug était présent depuis 010 mais masqué car le flux OTP complet
n'avait jamais pu être testé de bout en bout (bloqué par Zavu) — la connexion admin (sans OTP) l'a
révélé immédiatement.

## Definition of Done
- [x] Connexion admin testée en réel de bout en bout (clé valide → tableau de bord).
- [x] Navigation entre pages admin sans perte de session, testée en réel.
- [x] Modération par identifiant testée en réel ("introuvable" sur id inexistant).
- [x] Bug de prerendering corrigé sur toutes les pages concernées (010/011/012), testé en réel.

## Prochaine étape
Un design complet (Stitch) a été fourni par l'utilisateur, à respecter à 100% — remplacera
progressivement le Bootstrap par défaut sur les trois frontends déjà construits.
