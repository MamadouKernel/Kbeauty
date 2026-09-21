# Research: Historique des rendez-vous client et paiement en ligne

## Decision 1 — Table dédiée `transaction_rdv` plutôt que réutiliser `transaction`
**Decision**: Créer une nouvelle table `transaction_rdv`, distincte de `transaction`.
**Rationale**: `transaction` (migration 0001) porte `id_abonnement UUID NOT NULL REFERENCES abonnement
ON DELETE RESTRICT` — un paiement d'abonnement, structurellement. La rendre polymorphe (nullable
`id_abonnement` + nullable `id_rdv` + contrainte XOR) casserait une contrainte NOT NULL déjà en
production-like (docker) et compliquerait toutes les requêtes existantes de 008/009. Une table dédiée,
symétrique, est plus sûre et plus lisible (cohérent avec MERISE : une transaction de paiement RDV est
conceptuellement distincte d'une transaction d'abonnement, même si le vocabulaire "transaction" est
partagé).
**Alternatives considered**: (a) rendre `transaction.id_abonnement` nullable + ajouter `id_rdv`
nullable + `CHECK` XOR — rejeté, migration plus risquée sur une table déjà utilisée par 008/009 en
production-like, gain de simplicité marginal. (b) stocker le paiement RDV comme JSON dans `rdv` —
rejeté, viole la 1NF/MERISE et empêche l'indexation par `reference_externe`.

## Decision 2 — Réutilisation intégrale de `IPaymentGateway` (008)
**Decision**: Aucune nouvelle interface de gateway ; `InitierPaiementRdvUseCase` appelle directement
`IPaymentGateway.InitiateAsync(montant, description, ct)` déjà injecté dans le projet (implémentation
`WinPayerGateway`, échec explicite si non configuré — même pattern que Zavu).
**Rationale**: L'interface est déjà générique (montant + description en entrée, `PaymentInitiation`/
`PaymentVerification` en sortie) — rien dans sa signature ne présuppose un abonnement. La dupliquer
pour "paiement RDV" créerait une divergence inutile entre deux flux qui utilisent le même agrégateur.
**Alternatives considered**: Créer `IRdvPaymentGateway` séparé — rejeté (YAGNI, DRY : même agrégateur,
même contrat, seul le repository de persistance change).

## Decision 3 — Webhook unique, routage par tentative successive
**Decision**: `WebhookController.Callback` essaie d'abord `IAbonnementRepository.MarquerPaiementAsync`
(comportement 008 inchangé) ; si celui-ci retourne `false` (référence inconnue), essaie ensuite
`IRdvPaiementRepository.MarquerPaiementAsync` avec la même `reference_externe`. Chaque méthode reste
idempotente et ne fait rien si la référence ne lui appartient pas.
**Rationale**: WiniPayer n'expose qu'un seul callback URL configuré côté marchand ; il n'y a pas de
moyen de distinguer côté agrégateur "paiement d'abonnement" vs "paiement de RDV" avant réception. Les
UUID de facture WiniPayer sont générés par WiniPayer et donc non-collisionnants entre les deux flux.
**Alternatives considered**: Deux endpoints de callback distincts (`/webhooks/winipayer/callback` et
`/webhooks/winipayer/callback-rdv`) — rejeté, nécessiterait de reconfigurer l'URL de callback par type
de paiement côté WiniPayer au moment de la création du lien, ce qui n'est pas supporté simplement par
l'API "checkout/standard/create" telle qu'intégrée en 008 (un seul `callback_url` global par compte).

## Decision 4 — Idempotence par contrainte unique `id_rdv`
**Decision**: `transaction_rdv.id_rdv` porte une contrainte `UNIQUE`. `InitierPaiementRdvUseCase`
vérifie d'abord si une transaction existe déjà pour ce RDV ; si oui et qu'elle est encore `EN_COURS`,
retourne le lien de paiement existant (ou relance `InitiateAsync` pour un nouveau lien si le précédent
a expiré côté agrégateur — décision produit simplifiée pour le MVP : on ne relance pas automatiquement,
on retourne simplement l'état courant, cohérent avec FR-009 "ne doit pas créer de transaction
dupliquée").
**Rationale**: Directement testable, empêche par construction la double-facturation évoquée en SC-004.
**Alternatives considered**: Verrou applicatif (lock) au lieu d'une contrainte DB — rejeté, moins fiable
en cas de concurrence multi-instance ; la contrainte DB est la garantie la plus forte.

## Decision 5 — Historique client : pas de nouvelle vue matérialisée
**Decision**: `ListByClientAsync` fait un `SELECT ... FROM rdv JOIN etablissement JOIN prestation
LEFT JOIN transaction_rdv ... WHERE id_utilisateur_client = @idClient ORDER BY date_heure_debut DESC`.
**Rationale**: Volume attendu par client faible (cf. Assumptions du spec — pas de pagination requise
pour le MVP) ; une jointure simple suffit et évite la complexité d'une vue/dénormalisation prématurée
(YAGNI).
**Alternatives considered**: Vue SQL dédiée — rejeté, aucune preuve de besoin de performance à ce stade.
