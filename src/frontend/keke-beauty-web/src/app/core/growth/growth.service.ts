import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { EstablishmentSummary } from '../establishments/establishment.service';

// Shapes of the lot 7 services (`KekeBeauty.Infrastructure.Growth`). Written by hand until `npm run api:types` is next run.

export interface WaitlistEntry {
  readonly id: string;
  readonly establishmentId: string;
  readonly establishmentName: string;
  readonly establishmentSlug: string;
  readonly date: string;
  readonly serviceName: string | null;
  /** `Waiting`, `Notified`, `Cancelled` or `Expired` (the day went by). */
  readonly status: string;
  readonly clientName: string | null;
  readonly createdAt: string;
  readonly notifiedAt: string | null;
}

export interface LoyaltyProgram {
  readonly isActive: boolean;
  readonly pointsPer1000: number;
  readonly rewardThreshold: number;
  readonly rewardDescription: string;
  readonly activeSince: string | null;
}

export interface PublicLoyalty {
  readonly pointsPer1000: number;
  readonly rewardThreshold: number;
  readonly rewardDescription: string;
}

export interface LoyaltyLine {
  readonly kind: 'Earn' | 'Redeem';
  readonly points: number;
  readonly note: string | null;
  readonly at: string;
}

export interface LoyaltyCard {
  readonly establishmentId: string;
  readonly establishmentName: string;
  readonly establishmentSlug: string;
  readonly points: number;
  readonly rewardThreshold: number;
  readonly rewardDescription: string;
  readonly rewardsAvailable: number;
  readonly programActive: boolean;
  readonly history: readonly LoyaltyLine[];
}

export interface LoyaltyMember {
  readonly clientUserId: string;
  readonly clientName: string;
  readonly points: number;
  readonly rewardsAvailable: number;
  readonly lastActivityAt: string;
}

export interface GiftCard {
  readonly id: string;
  readonly establishmentId: string;
  readonly establishmentName: string;
  readonly establishmentSlug: string;
  readonly code: string;
  readonly initialAmount: number;
  readonly balance: number;
  readonly currencyCode: string;
  readonly recipientName: string;
  readonly buyerName: string | null;
  readonly message: string | null;
  /** `Active`, `Spent`, `Expired` or `Cancelled`. */
  readonly status: string;
  readonly expiresAt: string;
  readonly createdAt: string;
  readonly hasHolder: boolean;
  readonly redemptions: readonly { readonly amount: number; readonly at: string }[];
}

export interface NewGiftCard {
  readonly amount: number;
  readonly recipientName: string;
  readonly buyerName?: string | null;
  readonly message?: string | null;
  readonly validityMonths: number;
}

export interface GroupBooking {
  readonly id: string;
  readonly establishmentId: string;
  readonly establishmentName: string;
  readonly establishmentSlug: string;
  readonly clientName: string;
  readonly clientPhone: string | null;
  readonly eventType: string;
  readonly desiredStartsAt: string;
  readonly groupSize: number;
  readonly services: string;
  readonly message: string | null;
  /** `Pending`, `Accepted`, `Declined` or `CancelledByClient`. */
  readonly status: string;
  readonly quotedPrice: number | null;
  readonly partnerMessage: string | null;
  readonly createdAt: string;
  readonly respondedAt: string | null;
}

export interface NewGroupBooking {
  readonly establishmentId: string;
  readonly eventType: string;
  readonly desiredStartsAt: string;
  readonly groupSize: number;
  readonly services: string;
  readonly message: string | null;
  readonly phone: string;
}

export interface Recommendation {
  readonly establishment: EstablishmentSummary;
  readonly reason: string;
}

export const GROUP_EVENTS: Readonly<Record<string, string>> = {
  Wedding: 'Mariage',
  Birthday: 'Anniversaire',
  Baptism: 'Baptême',
  Corporate: 'Événement d’entreprise',
  Other: 'Autre événement',
};

export const GROUP_STATUSES: Readonly<Record<string, string>> = {
  Pending: 'En attente de réponse',
  Accepted: 'Acceptée',
  Declined: 'Refusée',
  CancelledByClient: 'Annulée',
};

export const GIFT_CARD_STATUSES: Readonly<Record<string, string>> = {
  Active: 'Valable',
  Spent: 'Épuisée',
  Expired: 'Expirée',
  Cancelled: 'Annulée',
};

export const WAITLIST_STATUSES: Readonly<Record<string, string>> = {
  Waiting: 'En attente d’un créneau',
  Notified: 'Créneau libéré : vous avez été prévenu(e)',
  Expired: 'Journée passée',
  Cancelled: 'Retirée',
};

/** Rewards a balance gives right now (mirrors the server rule). */
export function rewardsFor(points: number, threshold: number): number {
  return threshold > 0 && points > 0 ? Math.floor(points / threshold) : 0;
}

/** Share of the way to the next reward, 0 to 100, for a progress bar. */
export function progressToReward(points: number, threshold: number): number {
  if (threshold <= 0 || points <= 0) {
    return 0;
  }
  return Math.round(((points % threshold) / threshold) * 100);
}

/** Waiting list, loyalty cards, gift cards, group bookings and recommendations (lot 7). */
@Injectable({ providedIn: 'root' })
export class GrowthService {
  private readonly http = inject(HttpClient);

  // ───────── Waiting list ─────────

  joinWaitlist(
    establishmentId: string,
    date: string,
    serviceId: string | null,
  ): Promise<WaitlistEntry> {
    return firstValueFrom(
      this.http.post<WaitlistEntry>('/api/v1/waitlist', { establishmentId, date, serviceId }),
    );
  }

  myWaitlist(): Promise<WaitlistEntry[]> {
    return firstValueFrom(this.http.get<WaitlistEntry[]>('/api/v1/me/waitlist'));
  }

  leaveWaitlist(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/v1/me/waitlist/${id}`));
  }

  establishmentWaitlist(establishmentId: string): Promise<WaitlistEntry[]> {
    return firstValueFrom(
      this.http.get<WaitlistEntry[]>(`/api/v1/pro/establishments/${establishmentId}/waitlist`),
    );
  }

  // ───────── Loyalty ─────────

  publicLoyalty(slug: string): Promise<PublicLoyalty | null> {
    return firstValueFrom(
      this.http.get<PublicLoyalty>(`/api/v1/establishments/${slug}/loyalty`),
    ).catch(() => null);
  }

  myLoyalty(): Promise<LoyaltyCard[]> {
    return firstValueFrom(this.http.get<LoyaltyCard[]>('/api/v1/me/loyalty'));
  }

  /** The establishment's card, or null while none was ever set up (204). */
  loyaltyProgram(establishmentId: string): Promise<LoyaltyProgram | null> {
    return firstValueFrom(
      this.http.get<LoyaltyProgram | null>(`/api/v1/pro/establishments/${establishmentId}/loyalty`),
    );
  }

  saveLoyaltyProgram(
    establishmentId: string,
    program: Omit<LoyaltyProgram, 'activeSince'>,
  ): Promise<LoyaltyProgram> {
    return firstValueFrom(
      this.http.put<LoyaltyProgram>(
        `/api/v1/pro/establishments/${establishmentId}/loyalty`,
        program,
      ),
    );
  }

  loyaltyMembers(establishmentId: string): Promise<LoyaltyMember[]> {
    return firstValueFrom(
      this.http.get<LoyaltyMember[]>(
        `/api/v1/pro/establishments/${establishmentId}/loyalty/members`,
      ),
    );
  }

  redeemLoyalty(establishmentId: string, clientUserId: string): Promise<LoyaltyMember> {
    return firstValueFrom(
      this.http.post<LoyaltyMember>(
        `/api/v1/pro/establishments/${establishmentId}/loyalty/redeem`,
        { clientUserId },
      ),
    );
  }

  // ───────── Gift cards ─────────

  createGiftCard(establishmentId: string, card: NewGiftCard): Promise<GiftCard> {
    return firstValueFrom(
      this.http.post<GiftCard>(`/api/v1/pro/establishments/${establishmentId}/gift-cards`, card),
    );
  }

  giftCards(establishmentId: string): Promise<GiftCard[]> {
    return firstValueFrom(
      this.http.get<GiftCard[]>(`/api/v1/pro/establishments/${establishmentId}/gift-cards`),
    );
  }

  lookupGiftCard(establishmentId: string, code: string): Promise<GiftCard> {
    return firstValueFrom(
      this.http.get<GiftCard>(`/api/v1/pro/establishments/${establishmentId}/gift-cards/lookup`, {
        params: new HttpParams().set('code', code),
      }),
    );
  }

  redeemGiftCard(cardId: string, amount: number): Promise<GiftCard> {
    return firstValueFrom(
      this.http.post<GiftCard>(`/api/v1/pro/gift-cards/${cardId}/redeem`, {
        amount,
        appointmentId: null,
      }),
    );
  }

  cancelGiftCard(cardId: string): Promise<GiftCard> {
    return firstValueFrom(this.http.post<GiftCard>(`/api/v1/pro/gift-cards/${cardId}/cancel`, {}));
  }

  myGiftCards(): Promise<GiftCard[]> {
    return firstValueFrom(this.http.get<GiftCard[]>('/api/v1/me/gift-cards'));
  }

  claimGiftCard(code: string): Promise<GiftCard> {
    return firstValueFrom(this.http.post<GiftCard>('/api/v1/me/gift-cards', { code }));
  }

  // ───────── Group bookings ─────────

  requestGroup(request: NewGroupBooking): Promise<GroupBooking> {
    return firstValueFrom(this.http.post<GroupBooking>('/api/v1/group-bookings', request));
  }

  myGroupBookings(): Promise<GroupBooking[]> {
    return firstValueFrom(this.http.get<GroupBooking[]>('/api/v1/me/group-bookings'));
  }

  cancelGroupBooking(id: string): Promise<GroupBooking> {
    return firstValueFrom(
      this.http.post<GroupBooking>(`/api/v1/me/group-bookings/${id}/cancel`, {}),
    );
  }

  establishmentGroupBookings(establishmentId: string): Promise<GroupBooking[]> {
    return firstValueFrom(
      this.http.get<GroupBooking[]>(`/api/v1/pro/establishments/${establishmentId}/group-bookings`),
    );
  }

  answerGroupBooking(
    id: string,
    accept: boolean,
    quotedPrice: number | null,
    message: string,
  ): Promise<GroupBooking> {
    return firstValueFrom(
      this.http.post<GroupBooking>(`/api/v1/pro/group-bookings/${id}/answer`, {
        accept,
        quotedPrice,
        message,
      }),
    );
  }

  // ───────── Recommendations ─────────

  recommendations(
    position: { lat: number; lng: number } | null,
    count = 8,
  ): Promise<Recommendation[]> {
    let params = new HttpParams().set('count', count);
    if (position) {
      params = params.set('lat', position.lat).set('lng', position.lng);
    }
    return firstValueFrom(this.http.get<Recommendation[]>('/api/v1/recommendations', { params }));
  }
}
