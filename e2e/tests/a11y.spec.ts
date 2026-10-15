import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';

/**
 * Automated WCAG 2.1 A and AA checks (axe-core) on the public pages. They find what a tool can find (contrast, names, roles,
 * landmarks, labels, headings order); they do not replace a screen-reader walkthrough, which is out of scope here.
 */
const PAGES = [
  '/',
  '/etablissements',
  '/etablissements/maison-eclat-coiffure',
  '/tarifs',
  '/cgu',
  '/confidentialite',
  '/mentions-legales',
  '/connexion',
  '/inscription',
  '/mot-de-passe-oublie',
];

for (const path of PAGES) {
  test(`${path} has no accessibility violation`, async ({ page }) => {
    await page.goto(path);
    await page.waitForLoadState('networkidle');
    const results = await new AxeBuilder({ page: page as unknown } as ConstructorParameters<typeof AxeBuilder>[0])
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'best-practice'])
      .analyze();
    const summary = results.violations.map((v) => ({
      rule: v.id,
      impact: v.impact,
      help: v.help,
      targets: v.nodes.slice(0, 3).map((n) => n.target.join(' ')),
    }));
    expect(summary).toEqual([]);
  });
}
