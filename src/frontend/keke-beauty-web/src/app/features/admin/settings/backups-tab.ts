import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, resource, signal } from '@angular/core';
import { AdminApiService, Backup } from '../../../core/admin/admin-api.service';
import { problemMessage } from '../../../core/auth/auth.models';

const KILOBYTE = 1024;
const MEGABYTE = KILOBYTE * 1024;

const STATUS_LABELS: Record<string, string> = {
  Running: 'En cours',
  Succeeded: 'Réussie',
  Failed: 'Échec',
};

/** Database backups for the SuperAdmin: take a dump now, download it, delete it. The schedule and retention are server settings. */
@Component({
  selector: 'app-backups-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe],
  styles: `
    .hint {
      margin: 0;
      font-size: var(--fs-sm);
      color: var(--kb-ink-muted);
    }
  `,
  template: `
    <section class="admin-card" aria-labelledby="backups-title">
      <h2 id="backups-title">Sauvegardes de la base de données</h2>
      <div class="admin-filters">
        <button type="button" class="btn btn-primary" [disabled]="busy()" (click)="create()">
          {{ busy() ? 'Sauvegarde en cours…' : 'Créer une sauvegarde maintenant' }}
        </button>
      </div>
      <p class="hint">
        Fichier au format PostgreSQL compressé (<code>pg_restore</code>), conservé dans un espace
        privé. Il contient toutes les données, dont les clés de chiffrement : gardez-le comme un
        secret. Les sauvegardes planifiées et la durée de conservation se règlent sur le serveur ;
        la restauration se fait en ligne de commande (guide de déploiement, §10).
      </p>
      @if (message(); as text) {
        <p role="status" class="badge" data-tone="ok">{{ text }}</p>
      }
      @if (error(); as text) {
        <p class="form-error" role="alert">{{ text }}</p>
      }

      @if (backups.hasValue()) {
        @if (backups.value().length === 0) {
          <p>Aucune sauvegarde pour l'instant.</p>
        } @else {
          <div class="table-scroll">
            <table class="admin-table">
              <thead>
                <tr>
                  <th scope="col">Date</th>
                  <th scope="col">Origine</th>
                  <th scope="col">État</th>
                  <th scope="col">Taille</th>
                  <th scope="col"><span class="visually-hidden">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                @for (b of backups.value(); track b.id) {
                  <tr>
                    <td>{{ b.startedAt | date: 'dd/MM/yyyy HH:mm' }}</td>
                    <td>{{ b.kind === 'Scheduled' ? 'Planifiée' : 'Manuelle' }}</td>
                    <td>
                      <span class="badge" [attr.data-tone]="tone(b)">{{ label(b) }}</span>
                      @if (b.error) {
                        <br />
                        <small>{{ b.error }}</small>
                      }
                    </td>
                    <td>{{ size(b) }}</td>
                    <td>
                      @if (b.status === 'Succeeded') {
                        <button
                          type="button"
                          class="btn btn-ghost btn-sm"
                          [disabled]="busy()"
                          (click)="download(b)"
                        >
                          Télécharger
                        </button>
                      }
                      @if (b.status !== 'Running') {
                        <button
                          type="button"
                          class="btn btn-ghost btn-sm"
                          [disabled]="busy()"
                          (click)="remove(b)"
                        >
                          Supprimer
                        </button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      } @else if (backups.error()) {
        <p class="form-error" role="alert">Impossible de charger les sauvegardes.</p>
      }
    </section>
  `,
})
export class BackupsTab {
  private readonly api = inject(AdminApiService);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly backups = resource({ loader: () => this.api.backups() });

  protected label(backup: Backup): string {
    return STATUS_LABELS[backup.status] ?? backup.status;
  }

  protected tone(backup: Backup): string {
    return backup.status === 'Succeeded' ? 'ok' : backup.status === 'Failed' ? 'bad' : 'warn';
  }

  protected size(backup: Backup): string {
    if (backup.status !== 'Succeeded') {
      return '—';
    }
    return backup.sizeBytes >= MEGABYTE
      ? `${(backup.sizeBytes / MEGABYTE).toFixed(1)} Mo`
      : `${Math.max(1, Math.round(backup.sizeBytes / KILOBYTE))} Ko`;
  }

  protected create(): Promise<void> {
    return this.run(async () => {
      const backup = await this.api.createBackup();
      return backup.status === 'Succeeded'
        ? 'Sauvegarde terminée.'
        : `La sauvegarde a échoué : ${backup.error ?? 'erreur inconnue'}`;
    });
  }

  protected download(backup: Backup): Promise<void> {
    return this.run(async () => {
      await this.api.downloadBackup(backup.id);
      return 'Téléchargement lancé.';
    });
  }

  protected remove(backup: Backup): Promise<void> {
    if (!confirm('Supprimer définitivement cette sauvegarde ?')) {
      return Promise.resolve();
    }
    return this.run(async () => {
      await this.api.deleteBackup(backup.id);
      return 'Sauvegarde supprimée.';
    });
  }

  private async run(action: () => Promise<string>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    try {
      this.message.set(await action());
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
      this.backups.reload();
    }
  }
}
