# Plan: Lot 5 — Abonnements et paiement

## Backend
- **SharedKernel** : `IPaymentGateway` (création de transaction, vérification serveur à serveur, authenticité du webhook).
- **Billing** (domaine + EF, schéma `billing`) : `Plan` (+ `PlanFeatures` en jsonb), `PlanPriceHistory`, `Subscription`, `Invoice`, `Payment`, `PaymentWebhookEvent`, `InvoiceCounter`.
- **Catalog** : `Establishment.BookingAllowed` (autorisation issue du plan, maintenue par `EntitlementService`) ; recherche, fiche, calendrier et activation tiennent compte de `BookingEnabled && BookingAllowed`.
- **Infrastructure/Billing** : `EntitlementService`, `BillingService` (plans, souscription, paiement d'une facture, résiliation), `PaymentProcessor` (webhook idempotent, vérification, activation), `InvoiceNumbers`, `InvoicePdf`, `BillingMaintenance` (factures, relances, impayés, réconciliation), passerelles `FakePaymentGateway` et `CinetPayGateway`.
- **Api** : `/plans`, `/pro/establishments/{id}/subscription`, `/pro/subscriptions/{id}/cancel`, `/pro/invoices`, `/webhooks/payments/{provider}`, `/dev/payments/{tx}/simulate` (Fake seulement), `/admin/plans/{id}` ; limites par plan (médias, statistiques) ; migration `AddBilling` (plans, données existantes).
- Tests : unitaires (jeton CinetPay, numérotation, PDF, périodes, droits) ; intégration (parcours de souscription, idempotence, fraude, impayés avec horloge simulée, droits par plan, autorisations).

## Frontend
- `/pro/etablissements/:id/abonnement` : plans, périodicité, souscription, statut, factures (PDF, payer), résiliation ; page de retour avec suivi du statut.
- `/paiement/simulation` (développement) ; `/tarifs` public ; rappels « Premium » dans l'agenda (réservation, statistiques) et la galerie.
