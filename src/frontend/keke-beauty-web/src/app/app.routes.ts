import { Routes } from '@angular/router';
import { authGuard, guestGuard, roleGuard } from './core/auth/auth.guards';

export const routes: Routes = [
  {
    path: '',
    title: 'Keke Beauty — Salons, spa et onglerie près de chez vous',
    loadComponent: () => import('./features/public/home/home').then((m) => m.Home),
  },
  {
    path: 'connexion',
    title: 'Connexion — Keke Beauty',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/login/login').then((m) => m.Login),
  },
  {
    path: 'inscription',
    title: 'Créer un compte — Keke Beauty',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/register/register').then((m) => m.Register),
  },
  {
    path: 'confirmation-email',
    title: 'Confirmation de l’adresse e-mail — Keke Beauty',
    loadComponent: () =>
      import('./features/auth/confirm-email/confirm-email').then((m) => m.ConfirmEmail),
  },
  {
    path: 'mot-de-passe-oublie',
    title: 'Mot de passe oublié — Keke Beauty',
    loadComponent: () =>
      import('./features/auth/forgot-password/forgot-password').then((m) => m.ForgotPassword),
  },
  {
    path: 'reinitialiser-mot-de-passe',
    title: 'Nouveau mot de passe — Keke Beauty',
    loadComponent: () =>
      import('./features/auth/reset-password/reset-password').then((m) => m.ResetPassword),
  },
  {
    path: 'verification',
    title: 'Vérification en deux étapes — Keke Beauty',
    loadComponent: () => import('./features/auth/two-factor/two-factor').then((m) => m.TwoFactor),
  },
  {
    path: 'compte',
    title: 'Mon compte — Keke Beauty',
    canActivate: [authGuard],
    loadComponent: () => import('./features/account/account').then((m) => m.Account),
  },
  {
    path: 'etablissements',
    title: 'Établissements — Keke Beauty',
    loadComponent: () => import('./features/public/search/search').then((m) => m.Search),
  },
  {
    path: 'etablissements/:slug/reserver',
    title: 'Réserver — Keke Beauty',
    canActivate: [authGuard],
    loadComponent: () => import('./features/booking/booking-page').then((m) => m.BookingPage),
  },
  {
    path: 'compte/rendez-vous',
    title: 'Mes rendez-vous — Keke Beauty',
    canActivate: [authGuard],
    loadComponent: () => import('./features/account/my-appointments').then((m) => m.MyAppointments),
  },
  {
    path: 'compte/favoris',
    title: 'Mes favoris — Keke Beauty',
    canActivate: [authGuard],
    loadComponent: () => import('./features/account/favorites').then((m) => m.Favorites),
  },
  {
    path: 'etablissements/:slug',
    title: 'Établissement — Keke Beauty',
    loadComponent: () =>
      import('./features/public/establishment/establishment').then((m) => m.Establishment),
  },
  {
    path: 'cgu',
    title: 'Conditions d’utilisation — Keke Beauty',
    data: { doc: 'cgu' },
    loadComponent: () => import('./features/public/legal/legal').then((m) => m.Legal),
  },
  {
    path: 'confidentialite',
    title: 'Politique de confidentialité — Keke Beauty',
    data: { doc: 'confidentialite' },
    loadComponent: () => import('./features/public/legal/legal').then((m) => m.Legal),
  },
  {
    path: 'mentions-legales',
    title: 'Mentions légales — Keke Beauty',
    data: { doc: 'mentions-legales' },
    loadComponent: () => import('./features/public/legal/legal').then((m) => m.Legal),
  },
  {
    path: 'tarifs',
    title: 'Tarifs — Keke Beauty',
    loadComponent: () => import('./features/public/pricing/pricing').then((m) => m.Pricing),
  },
  {
    path: 'paiement/simulation',
    title: 'Paiement simulé — Keke Beauty',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/billing/payment-simulation').then((m) => m.PaymentSimulation),
  },
  {
    path: 'pro',
    canActivate: [authGuard, roleGuard('PartnerOwner')],
    children: [
      {
        path: '',
        title: 'Mes établissements — Keke Beauty',
        loadComponent: () => import('./features/pro/pro-home').then((m) => m.ProHome),
      },
      {
        path: 'etablissements/:id',
        title: 'Inscription d’un établissement — Keke Beauty',
        loadComponent: () => import('./features/pro/wizard/wizard').then((m) => m.Wizard),
      },
      {
        path: 'etablissements/:id/prestations',
        title: 'Prestations et tarifs — Keke Beauty',
        loadComponent: () =>
          import('./features/pro/catalog/catalog-page').then((m) => m.CatalogPage),
      },
      {
        path: 'etablissements/:id/agenda',
        title: 'Agenda — Keke Beauty',
        loadComponent: () => import('./features/pro/agenda/agenda-page').then((m) => m.AgendaPage),
      },
      {
        path: 'etablissements/:id/abonnement',
        title: 'Abonnement — Keke Beauty',
        loadComponent: () =>
          import('./features/pro/billing/subscription-page').then((m) => m.SubscriptionPage),
      },
      {
        path: 'etablissements/:id/paiements',
        title: 'Paiements des rendez-vous — Keke Beauty',
        loadComponent: () =>
          import('./features/pro/payments/payments-page').then((m) => m.ProPayments),
      },
      {
        path: 'etablissements/:id/avis',
        title: 'Avis clients — Keke Beauty',
        loadComponent: () => import('./features/pro/reviews/pro-reviews').then((m) => m.ProReviews),
      },
      {
        path: 'etablissements/:id/galerie',
        title: 'Galerie photos — Keke Beauty',
        loadComponent: () => import('./features/pro/gallery/pro-gallery').then((m) => m.ProGallery),
      },
    ],
  },
  {
    path: 'admin',
    canActivate: [authGuard, roleGuard('Admin', 'SuperAdmin')],
    loadComponent: () => import('./features/admin/admin-shell').then((m) => m.AdminShell),
    children: [
      {
        path: '',
        title: 'Administration — Keke Beauty',
        loadComponent: () => import('./features/admin/admin-home').then((m) => m.AdminHome),
      },
      {
        path: 'kyc',
        title: 'Dossiers KYC — Keke Beauty',
        loadComponent: () => import('./features/admin/kyc-queue').then((m) => m.KycQueue),
      },
      {
        path: 'kyc/:id',
        title: 'Examen d’un dossier — Keke Beauty',
        loadComponent: () => import('./features/admin/kyc-review').then((m) => m.KycReview),
      },
      {
        path: 'utilisateurs',
        title: 'Utilisateurs — Keke Beauty',
        loadComponent: () => import('./features/admin/users-page').then((m) => m.UsersPage),
      },
      {
        path: 'etablissements',
        title: 'Établissements — Keke Beauty',
        loadComponent: () =>
          import('./features/admin/establishments-page').then((m) => m.EstablishmentsPage),
      },
      {
        path: 'finances',
        title: 'Finances — Keke Beauty',
        loadComponent: () => import('./features/admin/finance-page').then((m) => m.FinancePage),
      },
      {
        path: 'reversements',
        title: 'Reversements — Keke Beauty',
        loadComponent: () => import('./features/admin/payouts-page').then((m) => m.PayoutsPage),
      },
      {
        path: 'parametres',
        title: 'Paramètres — Keke Beauty',
        loadComponent: () =>
          import('./features/admin/settings/settings-page').then((m) => m.SettingsPage),
      },
      {
        path: 'journal',
        title: 'Journal d’audit — Keke Beauty',
        loadComponent: () => import('./features/admin/audit-page').then((m) => m.AuditPage),
      },
      {
        path: 'administrateurs',
        title: 'Administrateurs — Keke Beauty',
        canActivate: [roleGuard('SuperAdmin')],
        loadComponent: () => import('./features/admin/admins-page').then((m) => m.AdminsPage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
