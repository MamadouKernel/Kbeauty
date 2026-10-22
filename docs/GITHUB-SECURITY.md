# Sécurité du dépôt GitHub et de GitGuardian : à régler (22 octobre 2026)

Ce qui est **dans le dépôt** est déjà en place (fichiers versionnés). Ce qui est **dans ton compte** GitHub / GitGuardian se règle
par clics : le chemin exact est donné ci-dessous. Coche au fur et à mesure.

## 1. Déjà dans le dépôt (rien à faire)
| Fichier | Rôle |
|---|---|
| `.github/workflows/ci.yml` | droits en lecture seule, actions épinglées par empreinte de commit, délais maximum, CI aussi sur `v1.0.1`, scan de secrets sur tout l'historique |
| `.github/workflows/codeql.yml` | analyse statique C# et TypeScript (requêtes `security-extended`), chaque semaine et à chaque pull request |
| `.github/workflows/dependency-review.yml` | une pull request qui ajoute une dépendance vulnérable (gravité haute) échoue |
| `.github/dependabot.yml` | mises à jour de paquets, images Docker et actions |
| `.github/CODEOWNERS`, `pull_request_template.md` | revue obligatoire des zones sensibles, liste de contrôle de sécurité |
| `.github/rulesets/protect-main.json` | règle de protection des branches `main` et `v1.0.1`, à importer (voir 2.4) |
| `.gitleaks.toml`, `.gitleaks-baseline.json`, hook `pre-commit` | scan local et CI des secrets |
| `.gitguardian.yaml` | exclusions de GitGuardian limitées aux fichiers générés |
| `SECURITY.md` | comment signaler une faille, périmètre, versions couvertes |

## 2. À régler dans GitHub (dépôt `Kbeauty` → **Settings**)
1. **Code security** (menu de gauche) : activer
   - *Private vulnerability reporting* (le lien « Report a vulnerability » de `SECURITY.md`) ;
   - *Dependency graph* ;
   - *Dependabot alerts* et *Dependabot security updates* ;
   - *Secret protection* → **Secret scanning** et **Push protection** (bloque un `git push` qui contient un secret) ;
   - *Code scanning* : rien à faire, le workflow CodeQL envoie ses résultats.
2. **Actions → General** :
   - *Workflow permissions* : **Read repository contents** (et décocher « Allow GitHub Actions to create and approve pull requests ») ;
   - *Actions permissions* : « Allow … and select non-… actions » : autoriser les actions GitHub et celles du dépôt ;
   - *Fork pull request workflows* : exiger une approbation pour tout contributeur extérieur.
3. **General** : cocher *Automatically delete head branches* ; décocher *Allow merge commits* si tu préfères un historique linéaire.
4. **Rules → Rulesets → New ruleset → Import a ruleset** : choisir `.github/rulesets/protect-main.json`. Effets : `main` et `v1.0.1` ne
   peuvent plus être supprimées ni réécrites (**plus de push forcé**), les changements passent par une pull request dont les
   contrôles `secrets`, `backend` et `frontend` sont verts. Les administrateurs peuvent encore passer par une pull request.
   *Attention* : une fois la règle active, un push direct sur ces branches est refusé ; passe par une branche et une pull request.
5. **Compte** (Settings de ton profil → *Password and authentication*) : authentification à deux facteurs **par clé de sécurité ou
   application** (pas seulement SMS) et codes de secours rangés hors ligne.

## 3. GitGuardian
1. Révoquer d'abord la clé exposée (WiniPayer : tableau de bord → clés, régénérer, mettre les nouvelles dans `.env`).
2. Ouvrir l'incident (lien reçu par e-mail) → **Resolve** → raison **Revoked** (la clé n'est plus valide).
3. Vérifier qu'il n'y a plus d'incident ouvert pour ce dépôt : tableau de bord → *Incidents* → filtre *Open*.
4. Intégration GitHub de GitGuardian : *Settings → Integrations* de GitGuardian, périmètre limité au dépôt `Kbeauty`, notifications par e-mail.
5. Facultatif : `ggshield` en pré-commit en plus de gitleaks (`pipx install ggshield`, clé personnelle GitGuardian, jamais dans le dépôt).

## 4. Secrets à considérer comme compromis et à renouveler
| Secret | Où il a été exposé | Action |
|---|---|---|
| Clés WiniPayer (production et privée) | affichées dans une conversation de travail | régénérer, mettre dans `.env` |
| **Clé de protection des données de la V1** (`src/KekeBeauty.Web/data-protection-keys/…xml`, branche `main`) | **fichier versionné, public, clé en clair** | voir 5 |
| Mot de passe du compte de démonstration local | donné en conversation | à changer si ce compte existe ailleurs que sur ta machine |

## 5. Clé de protection des données de la V1 (branche `main`)
Le dossier `data-protection-keys` de la V1 contient une clé **non chiffrée** (`AES_256_CBC`, valable jusqu'au 19/12/2026) dans un
dépôt public : toute personne qui la lit peut forger ou lire les cookies et jetons protégés par la V1.
1. Sur le serveur de la V1 (kekebeauty.duckdns.org) : supprimer le dossier des clés et redémarrer l'application : une nouvelle clé est
   créée (les sessions en cours sont déconnectées, c'est voulu).
2. Retirer le fichier du dépôt et l'ignorer (commit préparé sur `main`, à pousser quand tu le décides).
3. La clé reste lisible dans l'historique tant que les anciens commits existent : elle est donc **révoquée par la rotation**, pas par la suppression.

## 6. Contrôles de routine
- Chaque lundi : onglet *Security* (alertes Dependabot, code scanning, secrets) ; fusionner ou rejeter les pull requests Dependabot.
- À chaque nouvelle intégration : refaire un passage de sécurité et le consigner dans `docs/PENTEST.md`.
- Avant la production : `docs/PRODUCTION-CHECKLIST.md`, puis un audit indépendant.
