import { APIRequestContext, expect, test } from '@playwright/test';
import { Account, Salon, createAccount, createSalon } from '../helpers/api';
import { expectNoHorizontalScroll, signIn } from '../helpers/ui';

interface Plan {
  id: string;
  code: string;
  name: string;
}

let request: APIRequestContext;
let admin: Account;
const created: Plan[] = [];

test.beforeAll(async ({ playwright, baseURL }) => {
  request = await playwright.request.newContext({ baseURL });
  admin = await createAccount(request, 'Client', 'Admin');
});

test.afterAll(async () => {
  // Withdraw the test plans from sale so that the public pricing page is left as it was.
  for (const plan of created) {
    const current = (await (await request.get('/api/v1/admin/plans', { headers: admin.auth })).json()) as Record<string, unknown>[];
    const body = current.find((p) => p['id'] === plan.id);
    if (body) {
      await request.put(`/api/v1/admin/plans/${plan.id}`, { headers: admin.auth, data: { ...body, isActive: false } });
    }
  }

  await request.dispose();
});

async function createPlan(prefix: string, monthlyPrice: number, trialDays = 0): Promise<Plan> {
  const response = await request.post('/api/v1/admin/plans', {
    headers: admin.auth,
    data: {
      name: `${prefix} ${Date.now().toString(36)}${Math.random().toString(36).slice(2, 4)}`,
      monthlyPrice,
      annualPrice: monthlyPrice * 10,
      features: { booking: true, videos: false, maxMedia: 15, featured: false, stats: true, maxVideos: 0 },
      isActive: true,
      description: 'Formule de test',
      commitmentMonths: 3,
      trialDays,
    },
  });
  expect(response.ok(), await response.text()).toBeTruthy();
  const plan = (await response.json()) as Plan;
  created.push(plan);
  return plan;
}

test.describe('Free trial and change of plan', () => {
  test('a partner tries a plan for free, then stops the trial', async ({ page }) => {
    const plan = await createPlan('Essai', 5000, 7);
    const salon: Salon = await createSalon(request, admin, { subscribe: false });
    await signIn(page, salon.owner);
    await page.goto(`/pro/etablissements/${salon.id}/abonnement`);
    await expectNoHorizontalScroll(page);

    const card = page.locator('article.plan').filter({ hasText: plan.name });
    await expect(card).toContainText('Essai gratuit de 7 jours');
    await card.getByRole('button', { name: 'Essayer gratuitement' }).click();

    const current = page.locator('section.current').first();
    await expect(current).toContainText('Essai gratuit jusqu');
    await expect(current).toContainText(plan.name);

    // The trial is not offered a second time for this plan.
    await page.getByRole('button', { name: "Arrêter l'essai" }).click();
    await expect(page.getByText('Votre abonnement')).toHaveCount(0);
    const again = page.locator('article.plan').filter({ hasText: plan.name });
    await expect(again.getByRole('button', { name: 'Souscrire et payer' })).toBeVisible();
    await expect(again.getByRole('button', { name: 'Essayer gratuitement' })).toHaveCount(0);
  });

  test('an admin creates a promotion code and a partner pays the discounted price', async ({ page, browser, baseURL }) => {
    const plan = await createPlan('Promo', 10000);
    const salon: Salon = await createSalon(request, admin, { subscribe: false });
    const code = `E2E${Date.now().toString(36)}`.toUpperCase();

    // The administrator creates the code in the back-office.
    const adminAccount = await createAccount(request, 'Client', 'Admin');
    const adminContext = await browser.newContext({ baseURL, locale: 'fr-FR' });
    const adminPage = await adminContext.newPage();
    await signIn(adminPage, adminAccount);
    await adminPage.goto('/admin/parametres');
    await adminPage.getByRole('button', { name: 'Promotions' }).click();
    await adminPage.getByLabel('Code', { exact: true }).fill(code);
    await adminPage.getByLabel('Remise (%)').fill('20');
    await adminPage.getByRole('button', { name: 'Créer le code' }).click();
    await expect(adminPage.getByText('Code promo créé.')).toBeVisible();
    await expect(adminPage.getByRole('row').filter({ hasText: code })).toContainText('20 %');
    await adminContext.close();

    // The partner applies it, sees the new price, and the invoice is for the discounted amount.
    await signIn(page, salon.owner);
    await page.goto(`/pro/etablissements/${salon.id}/abonnement`);
    await page.locator('#promo-code').fill(code.toLowerCase());
    await page.getByRole('button', { name: 'Appliquer' }).click();
    await expect(page.getByText(`Code ${code} appliqué.`)).toBeVisible();
    const card = page.locator('article.plan').filter({ hasText: plan.name });
    await expect(card.locator('s.was')).toContainText(/10[\s  ]000/);
    await expect(card).toContainText(/8[\s  ]000/);

    await card.getByRole('button', { name: 'Souscrire et payer' }).click();
    await page.getByRole('button', { name: 'Payer', exact: true }).click();
    await expect(page.getByRole('row').filter({ hasText: 'KB-' })).toContainText(/8[\s  ]000/);

    // A wrong code is refused with an explanation.
    const other: Salon = await createSalon(request, admin, { subscribe: false });
    await page.context().clearCookies();
    await signIn(page, other.owner);
    await page.goto(`/pro/etablissements/${other.id}/abonnement`);
    await page.locator('#promo-code').fill('CODE-INCONNU');
    await page.getByRole('button', { name: 'Appliquer' }).click();
    await expect(page.getByText(/invalide, expiré/)).toBeVisible();
  });

  test('an upgrade is paid with the unused part credited, then a downgrade is scheduled and cancelled', async ({ page }) => {
    const small = await createPlan('Petit', 5000);
    const big = await createPlan('Grand', 10000);
    const salon: Salon = await createSalon(request, admin, { subscribe: false });
    await signIn(page, salon.owner);
    await page.goto(`/pro/etablissements/${salon.id}/abonnement`);

    await page.locator('article.plan').filter({ hasText: small.name }).getByRole('button', { name: 'Souscrire et payer' }).click();
    await page.getByRole('button', { name: 'Payer', exact: true }).click();
    const current = page.locator('section.current').first();
    await expect(current).toContainText(small.name);
    await expect(current).toContainText('Actif');

    // Upgrade: the quote shows the credit before anything is paid.
    await page.getByRole('button', { name: 'Changer de formule' }).click();
    await page.locator('article.plan').filter({ hasText: big.name }).getByRole('button', { name: 'Choisir cette formule' }).click();
    await expect(page.getByRole('heading', { name: 'Confirmer le changement' })).toBeVisible();
    await expect(page.getByText('Crédit de la période déjà payée')).toBeVisible();
    await expect(page.getByText('À payer maintenant')).toBeVisible();
    await page.getByRole('button', { name: 'Confirmer le changement' }).click();
    await page.getByRole('button', { name: 'Payer', exact: true }).click();
    await expect(page.locator('section.current').first()).toContainText(big.name);
    await expect(page.locator('section.current').first()).toContainText('Actif');

    // Downgrade: nothing to pay, scheduled for the end of the commitment, and it can be cancelled.
    await page.getByRole('button', { name: 'Changer de formule' }).click();
    await page.locator('article.plan').filter({ hasText: small.name }).getByRole('button', { name: 'Choisir cette formule' }).click();
    await expect(page.getByText('Rien à payer')).toBeVisible();
    await page.getByRole('button', { name: 'Confirmer le changement' }).click();
    await expect(page.getByText('Changement programmé')).toBeVisible();
    await expect(page.locator('section.current').first()).toContainText(big.name);

    await page.getByRole('button', { name: 'Annuler le changement' }).click();
    await expect(page.getByText('Changement programmé')).toHaveCount(0);
  });
});
