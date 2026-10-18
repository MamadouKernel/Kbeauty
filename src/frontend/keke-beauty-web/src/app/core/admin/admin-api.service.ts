import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { components } from '../api/api.generated';

type Schemas = components['schemas'];

export type AdminUser = Schemas['AdminUserDto'];
export type AdminUserDetail = Schemas['AdminUserDetail'];
export type AdminEstablishment = Schemas['AdminEstablishmentDto'];
export type AdminEstablishmentDetail = Schemas['AdminEstablishmentDetail'];
export type AuditEntry = Schemas['AuditEntryDto'];
export type FinanceSummary = Schemas['FinanceSummary'];
export type PastDue = Schemas['PastDueDto'];
export type CategoryAdmin = Schemas['CategoryAdminDto'];
export type CategoryInput = Schemas['CategoryInput'];
export type GeoAdmin = Schemas['GeoAdminDto'];
/** Optional fields are only used by some levels (a country needs its ISO code, a commune may carry coordinates). */
export interface GeoCreateInput {
  parentId?: string | null;
  name: string;
  iso2?: string;
  phonePrefix?: string;
  currencyCode?: string;
  latitude?: number;
  longitude?: number;
}
export type AppSettings = Schemas['AppSettingsDto'];
export type Template = Schemas['TemplateDto'];
export type ChannelsStatus = Schemas['ChannelsStatus'];
export type LegalSummary = Schemas['LegalDocumentSummary'];
export type LegalDocumentAdmin = Schemas['LegalDocumentAdminDto'];
export type LegalVersion = Schemas['LegalVersionDto'];
export type LegalInput = Schemas['LegalDocumentInput'];
export type Payout = Schemas['PayoutDto'];
export type PayoutRun = Schemas['PayoutRunReport'];
export type Refund = Schemas['RefundDto'];

/** A transfer to a collaborator (hand-written until the API types are regenerated). */
export interface CollaboratorPayoutRow {
  readonly id: string;
  readonly establishmentName: string;
  readonly collaboratorName: string;
  readonly commissionAmount: number;
  readonly tipAmount: number;
  readonly amount: number;
  readonly currencyCode: string;
  readonly operator: string;
  readonly accountMasked: string;
  readonly status: string;
  readonly createdAt: string;
}
export type Backup = Schemas['BackupDto'];
export type Plan = Schemas['PlanDto'];
export type PlanUpdate = Schemas['PlanUpdate'];
export type PromoCode = Schemas['PromoCodeDto'];
export type PromoCodeInput = Schemas['PromoCodeInput'];

export interface PageOf<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export type GeoLevel = 'countries' | 'regions' | 'cities' | 'communes';

function query(values: Record<string, string | number | undefined | null>): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(values)) {
    if (value !== undefined && value !== null && value !== '') {
      params = params.set(key, String(value));
    }
  }

  return params;
}

/** Back-office API (`/api/v1/admin`): accounts, moderation, finances, settings, audit, administrators. */
@Injectable({ providedIn: 'root' })
export class AdminApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/admin';

  // ───────── Users and administrators ─────────

  users(filter: {
    q?: string;
    role?: string;
    status?: string;
    page?: number;
  }): Promise<PageOf<AdminUser>> {
    return firstValueFrom(
      this.http.get<PageOf<AdminUser>>(`${this.base}/users`, { params: query(filter) }),
    );
  }

  user(id: string): Promise<AdminUserDetail> {
    return firstValueFrom(this.http.get<AdminUserDetail>(`${this.base}/users/${id}`));
  }

  suspendUser(id: string, reason: string): Promise<void> {
    return this.post(`${this.base}/users/${id}/suspend`, { reason });
  }

  reactivateUser(id: string, reason: string): Promise<void> {
    return this.post(`${this.base}/users/${id}/reactivate`, { reason });
  }

  admins(): Promise<AdminUser[]> {
    return firstValueFrom(this.http.get<AdminUser[]>(`${this.base}/admins`));
  }

  grantAdmin(email: string): Promise<AdminUser> {
    return firstValueFrom(this.http.post<AdminUser>(`${this.base}/admins`, { email }));
  }

  revokeAdmin(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.base}/admins/${id}`));
  }

  // ───────── Establishments ─────────

  establishments(filter: {
    q?: string;
    status?: string;
    page?: number;
  }): Promise<PageOf<AdminEstablishment>> {
    return firstValueFrom(
      this.http.get<PageOf<AdminEstablishment>>(`${this.base}/establishments`, {
        params: query(filter),
      }),
    );
  }

  establishment(id: string): Promise<AdminEstablishmentDetail> {
    return firstValueFrom(
      this.http.get<AdminEstablishmentDetail>(`${this.base}/establishments/${id}`),
    );
  }

  suspendEstablishment(id: string, reason: string): Promise<void> {
    return this.post(`${this.base}/establishments/${id}/suspend`, { reason });
  }

  reactivateEstablishment(id: string, reason: string): Promise<void> {
    return this.post(`${this.base}/establishments/${id}/reactivate`, { reason });
  }

  removeMedia(establishmentId: string, mediaId: string, reason: string): Promise<void> {
    return this.post(`${this.base}/establishments/${establishmentId}/media/${mediaId}/remove`, {
      reason,
    });
  }

  // ───────── Finance ─────────

  financeSummary(from?: string, to?: string): Promise<FinanceSummary> {
    return firstValueFrom(
      this.http.get<FinanceSummary>(`${this.base}/finance/summary`, {
        params: query({ from, to }),
      }),
    );
  }

  remind(subscriptionId: string): Promise<void> {
    return this.post(`${this.base}/finance/past-due/${subscriptionId}/remind`, {});
  }

  /** Downloads the payments CSV (the endpoint needs the bearer token, so the file is fetched then saved). */
  async downloadPaymentsCsv(from?: string, to?: string): Promise<void> {
    const blob = await firstValueFrom(
      this.http.get(`${this.base}/finance/payments.csv`, {
        params: query({ from, to }),
        responseType: 'blob',
      }),
    );
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = 'paiements.csv';
    link.click();
    URL.revokeObjectURL(url);
  }

  // ───────── Settings ─────────

  categories(): Promise<CategoryAdmin[]> {
    return firstValueFrom(this.http.get<CategoryAdmin[]>(`${this.base}/categories`));
  }

  createCategory(input: CategoryInput): Promise<unknown> {
    return firstValueFrom(this.http.post(`${this.base}/categories`, input));
  }

  updateCategory(id: string, input: CategoryInput): Promise<void> {
    return firstValueFrom(this.http.put<void>(`${this.base}/categories/${id}`, input));
  }

  geo(level: GeoLevel, parentId?: string): Promise<GeoAdmin[]> {
    return firstValueFrom(
      this.http.get<GeoAdmin[]>(`${this.base}/geo/${level}`, { params: query({ parentId }) }),
    );
  }

  createGeo(level: GeoLevel, input: GeoCreateInput): Promise<unknown> {
    return firstValueFrom(this.http.post(`${this.base}/geo/${level}`, input));
  }

  updateGeo(level: GeoLevel, id: string, name: string, isActive: boolean): Promise<void> {
    return firstValueFrom(
      this.http.put<void>(`${this.base}/geo/${level}/${id}`, { name, isActive }),
    );
  }

  settings(): Promise<AppSettings> {
    return firstValueFrom(this.http.get<AppSettings>(`${this.base}/settings`));
  }

  updateSettings(settings: AppSettings): Promise<AppSettings> {
    return firstValueFrom(this.http.put<AppSettings>(`${this.base}/settings`, settings));
  }

  templates(): Promise<Template[]> {
    return firstValueFrom(this.http.get<Template[]>(`${this.base}/templates`));
  }

  updateTemplate(code: string, title: string, body: string): Promise<void> {
    return firstValueFrom(
      this.http.put<void>(`${this.base}/templates/${encodeURIComponent(code)}`, { title, body }),
    );
  }

  resetTemplate(code: string): Promise<void> {
    return firstValueFrom(
      this.http.delete<void>(`${this.base}/templates/${encodeURIComponent(code)}`),
    );
  }

  plans(): Promise<Plan[]> {
    return firstValueFrom(this.http.get<Plan[]>(`${this.base}/plans`));
  }

  createPlan(plan: PlanUpdate): Promise<Plan> {
    return firstValueFrom(this.http.post<Plan>(`${this.base}/plans`, plan));
  }

  updatePlan(id: string, plan: PlanUpdate): Promise<Plan> {
    return firstValueFrom(this.http.put<Plan>(`${this.base}/plans/${id}`, plan));
  }

  promoCodes(): Promise<PromoCode[]> {
    return firstValueFrom(this.http.get<PromoCode[]>(`${this.base}/promo-codes`));
  }

  createPromoCode(code: PromoCodeInput): Promise<PromoCode> {
    return firstValueFrom(this.http.post<PromoCode>(`${this.base}/promo-codes`, code));
  }

  updatePromoCode(id: string, code: PromoCodeInput): Promise<PromoCode> {
    return firstValueFrom(this.http.put<PromoCode>(`${this.base}/promo-codes/${id}`, code));
  }

  // ───────── Channels (SMS, WhatsApp, push, e-mail) ─────────

  channels(): Promise<ChannelsStatus> {
    return firstValueFrom(this.http.get<ChannelsStatus>(`${this.base}/channels`));
  }

  sendTestSms(phone: string): Promise<unknown> {
    return firstValueFrom(this.http.post(`${this.base}/channels/sms/test`, { phone }));
  }

  createSender(name: string): Promise<string> {
    return firstValueFrom(
      this.http.post(`${this.base}/channels/letexto/senders`, { name }, { responseType: 'text' }),
    );
  }

  // ───────── Legal texts ─────────

  legalDocuments(): Promise<LegalSummary[]> {
    return firstValueFrom(this.http.get<LegalSummary[]>(`${this.base}/legal`));
  }

  legalDocument(slug: string): Promise<LegalDocumentAdmin> {
    return firstValueFrom(this.http.get<LegalDocumentAdmin>(`${this.base}/legal/${slug}`));
  }

  legalVersions(slug: string): Promise<LegalVersion[]> {
    return firstValueFrom(this.http.get<LegalVersion[]>(`${this.base}/legal/${slug}/versions`));
  }

  saveLegalDocument(slug: string, input: LegalInput): Promise<LegalDocumentAdmin> {
    return firstValueFrom(this.http.put<LegalDocumentAdmin>(`${this.base}/legal/${slug}`, input));
  }

  // ───────── Partner payouts and refunds ─────────

  payouts(): Promise<Payout[]> {
    return firstValueFrom(this.http.get<Payout[]>(`${this.base}/payouts`));
  }

  runPayouts(): Promise<PayoutRun> {
    return firstValueFrom(this.http.post<PayoutRun>(`${this.base}/payouts/run`, null));
  }

  payEstablishment(id: string): Promise<Payout> {
    return firstValueFrom(
      this.http.post<Payout>(`${this.base}/payouts/establishments/${id}`, null),
    );
  }

  collaboratorPayouts(): Promise<CollaboratorPayoutRow[]> {
    return firstValueFrom(
      this.http.get<CollaboratorPayoutRow[]>(`${this.base}/collaborator-payouts`),
    );
  }

  refunds(): Promise<Refund[]> {
    return firstValueFrom(this.http.get<Refund[]>(`${this.base}/refunds`));
  }

  retryRefund(id: string): Promise<void> {
    return this.post(`${this.base}/refunds/${id}/retry`, null);
  }

  markRefunded(id: string, reference: string): Promise<void> {
    return this.post(`${this.base}/refunds/${id}/manual`, { reference });
  }

  // ───────── Database backups (SuperAdmin) ─────────

  backups(): Promise<Backup[]> {
    return firstValueFrom(this.http.get<Backup[]>(`${this.base}/backups`));
  }

  createBackup(): Promise<Backup> {
    return firstValueFrom(this.http.post<Backup>(`${this.base}/backups`, null));
  }

  deleteBackup(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.base}/backups/${id}`));
  }

  /** The dump is fetched with the bearer token (a plain link would not carry it) and handed to the browser as a file. */
  async downloadBackup(id: string): Promise<void> {
    const response = await firstValueFrom(
      this.http.get(`${this.base}/backups/${id}/download`, {
        observe: 'response',
        responseType: 'blob',
      }),
    );
    const disposition = response.headers.get('content-disposition') ?? '';
    const name =
      /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)?.[1] ?? 'keke-beauty.dump';
    const url = URL.createObjectURL(response.body as Blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = decodeURIComponent(name);
    link.click();
    URL.revokeObjectURL(url);
  }

  // ───────── Audit ─────────

  audit(filter: {
    action?: string;
    entityType?: string;
    entityId?: string;
    actorId?: string;
    from?: string;
    to?: string;
    page?: number;
  }): Promise<PageOf<AuditEntry>> {
    return firstValueFrom(
      this.http.get<PageOf<AuditEntry>>(`${this.base}/audit`, { params: query(filter) }),
    );
  }

  private post(url: string, body: unknown): Promise<void> {
    return firstValueFrom(this.http.post<void>(url, body));
  }
}
