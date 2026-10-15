import type { components } from '../api/api.generated';

type Plan = components['schemas']['PlanDto'];

export interface PlanCondition {
  readonly label: string;
  /** False when the plan does not include it (shown struck through on the pricing pages). */
  readonly on: boolean;
}

/** What a plan includes and its limits, as a list for the pricing pages: the content of a plan comes from its settings. */
export function planConditions(plan: Plan): PlanCondition[] {
  const f = plan.features;
  return [
    { label: 'Prise de rendez-vous en ligne', on: f.booking },
    ...(f.booking
      ? [
          {
            label:
              f.maxResources == null
                ? 'Employés / ressources illimités'
                : `${f.maxResources} employé(s) ou ressource(s)`,
            on: true,
          },
          {
            label:
              f.maxAppointmentsPerMonth == null
                ? 'Rendez-vous illimités'
                : `Jusqu'à ${f.maxAppointmentsPerMonth} rendez-vous par mois`,
            on: true,
          },
        ]
      : []),
    { label: `Jusqu'à ${f.maxMedia} photos`, on: true },
    f.videos && f.maxVideos > 0
      ? { label: `Jusqu'à ${f.maxVideos} vidéo(s)`, on: true }
      : { label: 'Vidéos', on: false },
    { label: 'Statistiques de la fiche', on: f.stats },
    { label: 'Mise en avant dans l’annuaire', on: f.featured },
  ];
}

/** Same rule as the server: rounded down to a multiple of 5, never below 100 nor above the price. */
export function promoPrice(price: number, percent: number): number {
  if (percent <= 0 || price <= 0) {
    return price;
  }

  const discounted = Math.floor((price * (100 - percent)) / 100 / 5) * 5;
  return Math.min(price, Math.max(100, discounted));
}

/** "Promotion −30 % jusqu'au 31/12/2026", empty when the plan has no promotion running. */
export function promoLabel(plan: Plan): string {
  if (!plan.promoActive || plan.promoPercent <= 0) {
    return '';
  }

  const until = plan.promoEndsAt
    ? ` jusqu’au ${new Date(plan.promoEndsAt).toLocaleDateString('fr-FR')}`
    : '';
  return `Promotion −${plan.promoPercent} %${until}`;
}

/** "Essai gratuit de 14 jours", empty when the plan has no trial. */
export function trialLabel(days: number): string {
  return days > 0 ? `Essai gratuit de ${days} jours` : '';
}

/** "Engagement de 12 mois", or "Sans engagement" for a single month. */
export function commitmentLabel(months: number): string {
  return months <= 1 ? 'Sans engagement' : `Engagement de ${months} mois`;
}
