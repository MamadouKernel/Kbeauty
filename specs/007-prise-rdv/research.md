# Phase 0 — Research: Prise de Rendez-vous

## Décision 1 — Détection de chevauchement atomique en SQL (pas en mémoire)
- **Decision**: `INSERT INTO rdv ... SELECT ... WHERE NOT EXISTS (SELECT 1 FROM rdv WHERE
  id_etablissement=... AND statut_rdv IN ('DEMANDE','CONFIRME') AND chevauchement)` en une seule
  requête atomique (pas de lecture puis écriture séparées).
- **Rationale**: Élimine la course entre deux demandes simultanées (Edge Case) — une vérification
  applicative "lire puis écrire" laisserait une fenêtre de course entre les deux opérations.
- **Alternatives rejetées**: Vérifier en C# puis insérer — rejeté (race condition possible sous charge).

## Décision 2 — Chevauchement calculé via la durée de la prestation
- **Decision**: Un créneau `[début, début+durée)` chevauche un autre si `début1 < fin2 AND début2 < fin1`.
  La durée provient de `prestation.duree_minutes` de la prestation demandée pour chaque RDV comparé.
- **Rationale**: Formule standard d'intersection d'intervalles, cohérente avec le MPD déjà en place
  (`rdv.id_prestation` → `prestation.duree_minutes`).

## Décision 3 — `X-Client-Id` pour l'identification du client (dette technique déjà acceptée)
- **Decision**: Même mécanisme que `X-Partner-Id` (006) : l'`idUtilisateur` obtenu après OTP client
  est porté par le client à chaque appel de demande de RDV.
- **Rationale**: Cohérence avec la dette technique déjà documentée et acceptée (pas de session/JWT
  construite à ce stade).

## Décision 4 — Notification RDV via un notifier dédié (pas de réutilisation de IPartnerNotifier)
- **Decision**: Nouveau `IRdvNotifier`/`ZavuWhatsAppRdvNotifier`, même pattern que `IPartnerNotifier` (004).
- **Rationale**: Cohérent avec la Décision 5 de la feature 004 (éviter de coupler des responsabilités
  de notification différentes) ; duplication mineure acceptée pour la stabilité du code déjà livré.

Prêt pour la Phase 1.
