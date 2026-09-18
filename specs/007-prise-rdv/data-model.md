# Phase 1 — Data Model: Prise de Rendez-vous

Aucune nouvelle table. Réutilise `rdv` (`date_heure_debut`, `statut_rdv`, `id_utilisateur_client`,
`id_etablissement`, `id_prestation`).

## État et transitions (statut_rdv)
```text
(demande)  → DEMANDE
DEMANDE    → CONFIRME    (gerant valide, FR-004)
DEMANDE    → REFUSE      (gerant refuse, FR-004)
DEMANDE    → DEMANDE     (reprogrammation : date_heure_debut change, statut reste DEMANDE, FR-004)
```

## Règle de chevauchement (FR-003/FR-007)
Deux RDV du même établissement chevauchent si leurs intervalles `[début, début+durée_prestation)`
s'intersectent, en ne considérant que les RDV aux statuts `DEMANDE`/`CONFIRME`.
