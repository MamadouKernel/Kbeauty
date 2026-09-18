# Research: Modération back-office

## Décision 1 — Attribut orthogonal plutôt que nouvel enum de statut
**Décision**: `est_suspendu BOOLEAN` séparé sur `utilisateur`/`etablissement`, plutôt qu'un nouvel état
dans `statut_kyc_enum` (ex. `SUSPENDU`).
**Rationale**: la suspension est un état de modération orthogonal au KYC — un établissement `VALIDE`
peut être suspendu puis réactivé sans perdre son statut KYC (spec.md Edge Case). Fusionner les deux
créerait des transitions ambiguës (que devient `statut_kyc` a la reactivation ?).
**Alternatives rejetées**: enum combiné (complexifie toutes les requêtes existantes qui testent déjà
`statut_kyc = 'VALIDE'` isolément) ; table d'historique de modération (sur-ingénierie, non demandé
par le CDC).

## Décision 2 — Points d'application de la vérification
**Décision**: vérification directement dans les requêtes SQL existantes (`DirectoryRepository`,
`RdvRepository`, `AbonnementRepository`) et dans `RequestOtpUseCase`, plutôt qu'un filtre/middleware
transverse.
**Rationale**: cohérent avec le style déjà en place (`statut_kyc = 'VALIDE'` est déjà vérifié au
niveau SQL dans ces mêmes repositories) ; évite d'introduire un nouveau mécanisme générique pour
un besoin ponctuel (YAGNI).
**Alternatives rejetées**: `IActionFilter` global vérifiant la suspension sur chaque requête entrante
— nécessiterait une notion de session/identité uniforme qui n'existe pas encore dans le projet.

## Décision 3 — Idempotence de la suspension/réactivation (FR-009)
**Décision**: `UPDATE ... SET est_suspendu = @valeur` sans vérifier l'état courant ; retourne
simplement le nombre de lignes affectées pour distinguer "trouvé" de "introuvable" (`404`).
**Rationale**: un `UPDATE` sans condition sur l'état courant est naturellement idempotent (aucune
erreur si déjà dans l'état cible).
**Alternatives rejetées**: vérifier l'état avant modification (complexité inutile, race conditions
possibles sans bénéfice).
