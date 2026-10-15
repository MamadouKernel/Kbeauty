# Tasks: Lot 5 — Abonnements et paiement

## Backend
- [x] T501 Domaine Billing : plans (JSON de fonctionnalités), historique de prix, abonnements (prix figé), factures, paiements, événements webhook, compteur de numéros
- [x] T502 Port `IPaymentGateway` ; passerelles `Fake` (dev/tests) et `CinetPay` v2 (signature HMAC, non testée sur sandbox)
- [x] T503 `PaymentProcessor` : webhook idempotent, revérification serveur, contrôle montant/devise, activation
- [x] T504 `BillingService` : souscription, paiement de facture, résiliation à terme, factures PDF (écrivain PDF maison), édition des plans
- [x] T505 `EntitlementService` + drapeaux appliqués : réservation, statistiques, limite de photos, mise en avant ; impayé = fermé
- [x] T506 `BillingMaintenance` : factures suivantes, rappels, retard/impayé, fin d'engagement, réconciliation des paiements
- [x] T507 Migration `AddBilling`, endpoints, données de démo Premium
- [x] T508 Tests : 102 unitaires, 96 d'intégration

## Frontend
- [x] T511 `BillingService`, page abonnement, `/tarifs`, simulateur de paiement ; client TypeScript régénéré
- [x] T512 Indications Premium (statistiques) ; tests Vitest (113 au total)

## Limites connues
- CinetPay non éprouvé sur le sandbox ; e-mails et SMS seulement journalisés.
