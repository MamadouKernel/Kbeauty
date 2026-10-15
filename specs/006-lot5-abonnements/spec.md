# Spec: Lot 5 — Abonnements et paiement

Source : PRD §7.8 (BIL-01 à BIL-08), §8.2 (billing), §8.3, §10.2, §11. Objectif : souscription payée en bac à sable → abonnement actif → réservation débloquée ; impayé → réservation fermée.

## Adaptations par rapport au PRD
- **Pas de Hangfire** : une tâche de fond (`BillingMaintenance`, même principe que `AppointmentMaintenance`) génère les factures, les relances, les impayés et la réconciliation.
- **Pas de QuestPDF** (licence commerciale au-delà d'un seuil de chiffre d'affaires, même piège que PrimeNG 22 et ImageSharp) : les factures PDF sont produites par un petit générateur PDF interne (texte, police Helvetica standard).
- **Fournisseur** : `Payments:Provider` = `Fake` (développement : page de paiement simulée, interdit en production) ou `CinetPay` (API v2 ; clés `Payments:CinetPay:*`). Le code CinetPay est écrit d'après la documentation et **n'a pas été exécuté contre le bac à sable** (aucune clé disponible).
- **Relances** : notification in-app et e-mail (journalisé) au gérant, à J-3, J0, J+3 et J+7 de l'échéance ; SMS au lot 6 (le gérant n'a pas de numéro personnel dans son compte).
- **Changement de plan** en cours d'engagement : non géré (un seul abonnement actif par établissement) ; à prévoir avec le back-office.
- Les plans ne sont modifiables qu'à travers `PUT /api/v1/admin/plans/{id}` (interface au lot 6).

## Règles
- **Plans** (BIL-01) : `Gratuit` (fiche, 5 photos, prestations, Appeler, S'y rendre) et `Premium` (+ réservation, vidéos, 30 médias, mise en avant, statistiques) ; fonctionnalités = drapeaux par plan (`booking`, `videos`, `max_media`, `featured`, `stats`), jamais codées en dur. Montants fictifs.
- **Souscription** (BIL-02/03) : établissement publié, plan payant, périodicité mensuelle (12 échéances d'engagement) ou annuelle ; prix et périodicité **copiés dans l'abonnement** ; `Active` **uniquement sur notification de paiement validée par vérification serveur à serveur** (jamais sur le retour navigateur) ; fin d'engagement = début + 12 mois.
- **Webhook** (§10.2) : signature vérifiée, **idempotent** (`payment_webhook_events`), montant et devise recontrôlés, charge brute journalisée ; réconciliation des paiements `Pending` de plus de 30 minutes.
- **Échéances** (BIL-04) : facture générée 3 jours avant l'échéance, relances J-3, J0, J+3, J+7. **Impayé** (BIL-05) : après 7 jours de grâce, `PastDue` → fonctionnalités premium coupées (réservation fermée aux nouvelles demandes, RDV confirmés conservés, fiche visible en mode Gratuit) ; retour à `Active` dès régularisation.
- **Résiliation** (BIL-06) : prend effet à la fin de l'engagement. **Factures** (BIL-07) : `KB-AAAA-000001`, séquentielles sans trou, PDF téléchargeable.
- **Droits** : réservation (activation, calendrier public, nouvelles demandes), limite de médias, mise en avant et statistiques suivent le plan effectif (`Active` = plan souscrit, sinon Gratuit).

## Critères d'acceptation
- Souscription simulée payée → abonnement `Active`, facture `Paid` numérotée, réservation activable.
- Webhook rejoué : aucun effet supplémentaire ; webhook de succès alors que le fournisseur dit « refusé » : ignoré ; montant différent : refusé.
- Facture impayée : relances, `Overdue`, puis `PastDue` après la grâce → réservation fermée ; paiement → rouverte.
- Un changement de tarif par l'admin ne modifie pas les abonnements en cours.
- Un partenaire ne voit que ses abonnements et factures.
