# Spec: Lot 6 — Back-office complet

Source: PRD §7.9 (NOT-02) et §7.10 (ADM-01, 02, 04 à 07).

## Parcours
- Un admin cherche un compte, consulte sa fiche et le suspend ou le réactive en donnant un motif (3 à 500 caractères). La suspension ferme ses sessions. Un Admin ne touche ni aux autres Admin ni aux SuperAdmin ; personne ne se suspend soi-même.
- Un admin suspend ou rétablit une fiche d'établissement (motif obligatoire, propriétaire notifié, fiche retirée de l'annuaire) et peut retirer une photo non conforme.
- Un admin suit les finances : abonnements par plan, revenu récurrent mensuel, encaissements par moyen de paiement, impayés avec relance manuelle (une par heure et par abonnement), export CSV des paiements (cellules neutralisées contre l'injection de formules).
- Un admin gère catégories (nom, icône, ordre, activation) et zones (pays, régions, villes, communes, activation), les formules et tarifs, le délai de grâce d'impayé et les modèles de messages (variables `{etablissement}`, `{date}`…).
- Toute action est écrite au journal d'audit (qui, quoi, quand, IP) ; le journal est consultable avec filtres.
- Un SuperAdmin nomme ou retire des administrateurs (compte existant, e-mail confirmé) ; la double authentification reste obligatoire.

## Hors périmètre
- Journalisation des envois par fournisseur (coût SMS), conservation KYC configurable, pays hors Côte d'Ivoire dans les parcours publics.
