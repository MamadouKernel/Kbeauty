import { ChangeDetectionStrategy, Component, inject, resource, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LucidePlus } from '@lucide/angular';
import { AuthService } from '../../core/auth/auth.service';
import { PartnerService, STATUS_LABELS } from '../../core/partner/partner.service';

/** `/pro` — the partner's establishments with their publication status and what is still missing. */
@Component({
  selector: 'app-pro-home',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, LucidePlus],
  styles: `
    .page {
      display: grid;
      gap: 1.5rem;
      padding-block: clamp(1.5rem, 4vw, 3rem);
    }
    .head {
      display: flex;
      flex-wrap: wrap;
      align-items: end;
      justify-content: space-between;
      gap: 1rem;
    }
    .list {
      display: grid;
      gap: 1rem;
    }
    .card {
      display: grid;
      gap: 1rem;
      padding: 1rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: var(--kb-card);
      box-shadow: var(--shadow-soft);
      transition:
        transform 200ms var(--ease-out),
        box-shadow 200ms var(--ease-out);

      &:hover {
        transform: translateY(-2px);
        box-shadow: var(--shadow-lift);
      }

      @media (min-width: 640px) {
        grid-template-columns: 9rem 1fr;
      }
    }
    .cover {
      width: 100%;
      aspect-ratio: 4 / 3;
      border-radius: var(--radius-md);
      object-fit: cover;
      background: linear-gradient(135deg, var(--kb-primary-200), var(--kb-primary-400));
    }
    .body {
      display: grid;
      gap: 0.5rem;
      align-content: start;
    }
    .status {
      display: inline-block;
      width: fit-content;
      padding: 0.125rem 0.75rem;
      border-radius: var(--radius-pill);
      background: var(--kb-primary-50);
      font-size: var(--fs-xs);
      font-weight: 700;
      color: var(--kb-primary-700);
    }
    .status[data-status='Active'] {
      background: color-mix(in srgb, var(--kb-success) 12%, var(--kb-card));
      color: var(--kb-success);
    }
    .status[data-status='Rejected'],
    .status[data-status='Suspended'] {
      background: color-mix(in srgb, var(--kb-danger) 10%, var(--kb-card));
      color: var(--kb-danger);
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
      margin-top: 0.25rem;
    }
    .missing {
      padding-left: 1.25rem;
      list-style: disc;
      font-size: var(--fs-md);
      color: var(--kb-ink-muted);
    }
    .empty {
      display: grid;
      justify-items: start;
      gap: 1rem;
      padding: clamp(1.5rem, 4vw, 2.5rem);
      border: 1.5px dashed var(--kb-primary-200);
      border-radius: var(--radius-lg);
      background: var(--kb-card);
    }
  `,
  template: `
    <div class="container page">
      <div class="head">
        <div>
          <p class="eyebrow">Espace professionnel</p>
          <h1>Mes <em>établissements</em></h1>
          @if (user(); as me) {
            <p class="auth-lead">Bonjour {{ me.firstName }} 👋</p>
          }
        </div>
        <a class="btn btn-primary" routerLink="/pro/etablissements/nouveau">
          <svg lucidePlus aria-hidden="true"></svg>
          Inscrire un établissement
        </a>
      </div>

      @if (establishments.hasValue()) {
        @if (establishments.value().length === 0) {
          <section class="empty" aria-labelledby="empty-title">
            <h2 id="empty-title">Votre premier établissement</h2>
            <p>
              Renseignez votre salon, spa ou onglerie en quelques minutes : informations, position,
              horaires, photo et pièce d'identité. Après validation par notre équipe, il apparaît
              dans l'annuaire.
            </p>
            <a class="btn btn-primary" routerLink="/pro/etablissements/nouveau">Commencer</a>
          </section>
        } @else {
          <ul class="list">
            @for (e of establishments.value(); track e.id) {
              <li class="card">
                <img class="cover" [src]="e.coverUrl ?? ''" alt="" [hidden]="!e.coverUrl" />
                @if (!e.coverUrl) {
                  <div class="cover" aria-hidden="true"></div>
                }
                <div class="body">
                  <h2>{{ e.name }}</h2>
                  <span class="status" [attr.data-status]="e.status">{{ label(e.status) }}</span>
                  @if (e.reviewComment && (e.status === 'Draft' || e.status === 'Rejected')) {
                    <p class="form-error" role="status">
                      Commentaire de l'équipe : {{ e.reviewComment }}
                    </p>
                  }
                  @if (e.missing.length > 0) {
                    <ul class="missing">
                      @for (item of e.missing; track item.code) {
                        <li>{{ item.label }}</li>
                      }
                    </ul>
                  }
                  <div class="actions">
                    <a class="btn btn-primary btn-sm" [routerLink]="['/pro/etablissements', e.id]">
                      {{
                        e.status === 'Draft'
                          ? "Continuer l'inscription"
                          : e.status === 'Active'
                            ? 'Modifier'
                            : 'Voir le dossier'
                      }}
                    </a>
                    @if (e.status === 'Draft' || e.status === 'Active') {
                      <a
                        class="btn btn-ghost btn-sm"
                        [routerLink]="['/pro/etablissements', e.id, 'prestations']"
                        >Prestations</a
                      >
                      <a
                        class="btn btn-ghost btn-sm"
                        [routerLink]="['/pro/etablissements', e.id, 'galerie']"
                        >Galerie</a
                      >
                      @if (e.status === 'Active') {
                        <a
                          class="btn btn-ghost btn-sm"
                          [routerLink]="['/pro/etablissements', e.id, 'agenda']"
                          >Agenda</a
                        >
                        <a
                          class="btn btn-ghost btn-sm"
                          [routerLink]="['/pro/etablissements', e.id, 'abonnement']"
                          >Abonnement</a
                        >
                        <a
                          class="btn btn-ghost btn-sm"
                          [routerLink]="['/pro/etablissements', e.id, 'paiements']"
                          >Moyens de paiement</a
                        >
                        <a
                          class="btn btn-ghost btn-sm"
                          [routerLink]="['/pro/etablissements', e.id, 'finances']"
                          >Finances et reçus</a
                        >
                        <a
                          class="btn btn-ghost btn-sm"
                          [routerLink]="['/pro/etablissements', e.id, 'avis']"
                          >Avis clients</a
                        >
                        <a
                          class="btn btn-ghost btn-sm"
                          [routerLink]="['/pro/etablissements', e.id, 'comptoir']"
                          >Comptoir</a
                        >
                        <a
                          class="btn btn-ghost btn-sm"
                          [routerLink]="['/pro/etablissements', e.id, 'scanner']"
                          >Scanner un pass</a
                        >
                        <a
                          class="btn btn-ghost btn-sm"
                          [routerLink]="['/pro/etablissements', e.id, 'equipe']"
                          >Équipe</a
                        >
                        <a
                          class="btn btn-ghost btn-sm"
                          [routerLink]="['/pro/etablissements', e.id, 'litiges']"
                          >Litiges</a
                        >
                        <a
                          class="btn btn-ghost btn-sm"
                          [routerLink]="['/pro/etablissements', e.id, 'fidelite']"
                          >Fidélité et cartes cadeaux</a
                        >
                        <a
                          class="btn btn-ghost btn-sm"
                          [routerLink]="['/pro/etablissements', e.id, 'demandes']"
                          >Groupes et liste d’attente</a
                        >
                      }
                    }
                    @if (e.status === 'Active') {
                      <a class="btn btn-ghost btn-sm" [routerLink]="['/etablissements', e.slug]"
                        >Voir la fiche publique</a
                      >
                      <button
                        type="button"
                        class="btn btn-light btn-sm"
                        [attr.aria-expanded]="openQrSlug() === e.slug"
                        (click)="toggleQr(e.slug)"
                      >
                        {{ openQrSlug() === e.slug ? 'Masquer le kit partage' : 'QR Code & WhatsApp' }}
                      </button>
                    }
                  </div>
                  @if (e.status === 'Active' && openQrSlug() === e.slug) {
                    <aside
                      style="margin-top: 0.75rem; padding: 1rem; border: 1px dashed var(--kb-primary-300); border-radius: var(--radius-md); background: var(--kb-primary-50); display: grid; gap: 0.75rem;"
                      aria-label="Kit de partage et QR code comptoir"
                    >
                      <strong>Partagez votre lien de réservation ({{ e.name }})</strong>
                      <p style="margin: 0; font-size: var(--fs-sm); color: var(--kb-ink-muted);">
                        Affichez ce QR Code au comptoir de votre salon ou publiez le lien dans votre statut WhatsApp et votre bio Instagram.
                      </p>
                      @if (qrDataUrl(); as qrUrl) {
                        <div style="display: flex; flex-wrap: wrap; align-items: center; gap: 1rem;">
                          <img
                            [src]="qrUrl"
                            width="160"
                            height="160"
                            alt="QR code de réservation pour {{ e.name }}"
                            style="border-radius: var(--radius-sm); background: #fff; padding: 0.35rem;"
                          />
                          <div class="actions">
                            <a
                              class="btn btn-primary btn-sm"
                              [href]="qrUrl"
                              [attr.download]="'qr-keke-beauty-' + e.slug + '.png'"
                            >
                              Télécharger le QR Code (PNG)
                            </a>
                            <a
                              class="btn btn-light btn-sm"
                              [href]="whatsAppShareUrl(e.slug, e.name)"
                              target="_blank"
                              rel="noopener noreferrer"
                            >
                              Partager sur WhatsApp
                            </a>
                            <button
                              type="button"
                              class="btn btn-ghost btn-sm"
                              (click)="copyBookingLink(e.slug)"
                            >
                              {{ copiedSlug() === e.slug ? 'Lien copié ✓' : 'Copier le lien' }}
                            </button>
                          </div>
                        </div>
                      }
                    </aside>
                  }
                </div>
              </li>
            }
          </ul>
        }
      } @else if (establishments.error()) {
        <p class="form-error" role="alert">Impossible de charger vos établissements. Réessayez.</p>
      } @else {
        <p role="status">Chargement…</p>
      }
    </div>
  `,
})
export class ProHome {
  private readonly partner = inject(PartnerService);
  protected readonly user = inject(AuthService).user;
  protected readonly establishments = resource({ loader: () => this.partner.list() });
  protected readonly openQrSlug = signal<string | null>(null);
  protected readonly qrDataUrl = signal<string | null>(null);
  protected readonly copiedSlug = signal<string | null>(null);

  protected label(status: string): string {
    return STATUS_LABELS[status] ?? status;
  }

  protected bookingUrl(slug: string): string {
    const origin = typeof window !== 'undefined' ? window.location.origin : '';
    return `${origin}/etablissements/${encodeURIComponent(slug)}/reserver`;
  }

  protected whatsAppShareUrl(slug: string, name: string): string {
    const text = `Réservez votre prochain rendez-vous chez ${name} en quelques clics sur Keke Beauty : ${this.bookingUrl(slug)}`;
    return `https://wa.me/?text=${encodeURIComponent(text)}`;
  }

  protected async toggleQr(slug: string): Promise<void> {
    if (this.openQrSlug() === slug) {
      this.openQrSlug.set(null);
      return;
    }
    this.openQrSlug.set(slug);
    this.qrDataUrl.set(null);
    const { default: QRCode } = await import('qrcode');
    const url = await QRCode.toDataURL(this.bookingUrl(slug), {
      margin: 2,
      width: 320,
      color: { dark: '#36174f', light: '#ffffff' },
    });
    if (this.openQrSlug() === slug) {
      this.qrDataUrl.set(url);
    }
  }

  protected async copyBookingLink(slug: string): Promise<void> {
    if (typeof navigator !== 'undefined' && navigator.clipboard) {
      await navigator.clipboard.writeText(this.bookingUrl(slug));
      this.copiedSlug.set(slug);
      setTimeout(() => {
        if (this.copiedSlug() === slug) {
          this.copiedSlug.set(null);
        }
      }, 2500);
    }
  }
}
