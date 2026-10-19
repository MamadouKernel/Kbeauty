import { APIRequestContext, Page, expect, test } from "@playwright/test";
import { Account, Salon, createAccount, createSalon } from "../helpers/api";
import { expectNoHorizontalScroll, signIn } from "../helpers/ui";

let request: APIRequestContext;
let admin: Account;
let salon: Salon;

test.beforeAll(async ({ playwright, baseURL }) => {
  request = await playwright.request.newContext({ baseURL });
  admin = await createAccount(request, "Client", "Admin");
  salon = await createSalon(request, admin, { autoConfirm: true });
});

test.afterAll(async () => {
  await request.dispose();
});

/** A Monday-to-Saturday day at least `ahead` days from now (the test salon is open 09:00-18:00 UTC those days). */
function openDay(ahead: number): Date {
  const day = new Date();
  day.setUTCDate(day.getUTCDate() + ahead);
  while (day.getUTCDay() === 0) {
    day.setUTCDate(day.getUTCDate() + 1);
  }
  day.setUTCHours(0, 0, 0, 0);
  return day;
}

async function book(
  client: Account,
  serviceId: string,
  startsAt: Date,
): Promise<string> {
  const response = await request.post("/api/v1/appointments", {
    headers: client.auth,
    data: {
      establishmentId: salon.id,
      serviceIds: [serviceId],
      resourceId: null,
      startsAt: startsAt.toISOString(),
      phone: "0700001234",
      note: null,
    },
  });
  expect(response.ok(), await response.text()).toBeTruthy();
  return ((await response.json()) as { id: string }).id;
}

/** Opens the month of `day` in the booking calendar and selects it. */
async function selectDay(page: Page, day: Date): Promise<void> {
  if (day.getUTCMonth() !== new Date().getUTCMonth()) {
    await page.getByRole("button", { name: "Mois suivant" }).click();
  }
  const label = new Intl.DateTimeFormat("fr-FR", {
    weekday: "long",
    day: "numeric",
    month: "long",
    timeZone: "UTC",
  }).format(day);
  await page.getByRole("gridcell", { name: new RegExp(`^${label} :`) }).click();
}

test.describe("Beyond V1 (lot 7)", () => {
  test("a full day offers the waiting list and the client is told when a slot frees up", async ({
    page,
    browser,
    baseURL,
  }) => {
    test.setTimeout(240_000);
    const booker = await createAccount(request, "Client");
    const waiter = await createAccount(request, "Client");

    // Fill the whole day with one-hour visits.
    const day = openDay(4);
    const ids: string[] = [];
    for (let hour = 9; hour < 18; hour++) {
      const start = new Date(day);
      start.setUTCHours(hour);
      ids.push(await book(booker, salon.hourService, start));
    }

    await signIn(page, waiter);
    await page.goto(`/etablissements/${salon.slug}/reserver`);
    await page.getByRole("checkbox", { name: /Brushing/ }).check();
    await selectDay(page, day);
    await expect(page.getByText("Cette journée est complète.")).toBeVisible();
    await page
      .getByRole("button", { name: "Me prévenir si un créneau se libère" })
      .click();
    await expect(
      page.getByText(/vous serez prévenu\(e\) dès qu’un créneau se libère/),
    ).toBeVisible();

    // The booker cancels one visit: the waiting client is told.
    const cancelled = await request.post(
      `/api/v1/appointments/${ids[3]}/cancel`,
      { headers: booker.auth, data: {} },
    );
    expect(cancelled.ok(), await cancelled.text()).toBeTruthy();

    await page.goto("/compte/fidelite");
    await expect(
      page.getByText("Créneau libéré : vous avez été prévenu(e)"),
    ).toBeVisible();

    // The establishment saw who was waiting.
    const owner = await (await browser.newContext({ baseURL })).newPage();
    await signIn(owner, salon.owner);
    await owner.goto(`/pro/etablissements/${salon.id}/demandes`);
    await expect(owner.getByRole("cell", { name: "Prévenue" })).toBeVisible();
  });

  test("the owner sets up a loyalty card and sells a gift card the client follows from her account", async ({
    page,
    browser,
    baseURL,
  }) => {
    test.setTimeout(180_000);
    const client = await createAccount(request, "Client");

    await signIn(page, salon.owner);
    await page.goto(`/pro/etablissements/${salon.id}/fidelite`);
    await page.getByLabel("Points par tranche de 1 000 F").fill("10");
    await page.getByLabel("Points pour une récompense").fill("100");
    await page
      .getByLabel("Récompense", { exact: true })
      .fill("Un brushing offert");
    await page.getByRole("button", { name: "Enregistrer" }).click();
    await expect(
      page.getByText("Carte de fidélité enregistrée."),
    ).toBeVisible();

    await page.getByLabel("Montant (F)").fill("25000");
    await page.getByLabel("Pour", { exact: true }).fill("Aya Traoré");
    await page.getByRole("button", { name: "Créer la carte" }).click();
    const created = page.getByText(/Carte créée : code KB-/);
    await expect(created).toBeVisible();
    const code = /KB-[A-Z0-9]{4}-[A-Z0-9]{4}/.exec(
      (await created.textContent()) ?? "",
    )![0];

    // The public page shows the card.
    const visitor = await (await browser.newContext({ baseURL })).newPage();
    await visitor.goto(`/etablissements/${salon.slug}`);
    await expect(visitor.locator("p.loyalty")).toContainText(
      "Un brushing offert",
    );

    // The client adds the gift card to her account; the owner debits part of it.
    const clientPage = await (await browser.newContext({ baseURL })).newPage();
    await signIn(clientPage, client);
    await clientPage.goto("/compte/fidelite");
    await clientPage
      .getByLabel("Code reçu")
      .fill(code.toLowerCase().replaceAll("-", " "));
    await clientPage
      .getByRole("button", { name: "Ajouter à mon compte" })
      .click();
    await expect(clientPage.getByText(`Carte ${code} ajoutée`)).toBeVisible();

    await page.getByLabel("Retrouver une carte par son code").fill(code);
    await page.getByRole("button", { name: "Rechercher" }).click();
    await expect(
      page.getByText(new RegExp(`Carte ${code} : solde 25`)),
    ).toBeVisible();
    await page.getByLabel("Montant à débiter").first().fill("10000");
    await page.getByRole("button", { name: "Débiter" }).first().click();
    await expect(page.getByText(/débités : solde 15/)).toBeVisible();

    await clientPage.reload();
    await expect(
      clientPage.locator(".gift").filter({ hasText: code }),
    ).toContainText("15");
  });

  test("a client asks for a group and accepts nothing until the establishment sends a quote", async ({
    page,
    browser,
    baseURL,
  }) => {
    test.setTimeout(180_000);
    const client = await createAccount(request, "Client");

    await signIn(page, client);
    await page.goto(`/etablissements/${salon.slug}`);
    await page.getByRole("link", { name: "Groupe ou événement" }).click();
    await expect(
      page.getByRole("heading", { name: /Un événement chez/ }),
    ).toBeVisible();
    await page.getByLabel("Nombre de personnes").fill("6");
    await page.getByLabel("Téléphone").fill("0700001234");
    await page
      .getByLabel("Prestations souhaitées")
      .fill("Coiffure de la mariée et tresses pour 5 demoiselles");
    await page.getByRole("button", { name: "Envoyer la demande" }).click();
    await expect(
      page.getByText(/Demande envoyée pour 6 personnes/),
    ).toBeVisible();

    const owner = await (await browser.newContext({ baseURL })).newPage();
    await signIn(owner, salon.owner);
    await owner.goto(`/pro/etablissements/${salon.id}/demandes`);
    await owner.getByPlaceholder("Prix total (F)").fill("180000");
    await owner
      .getByPlaceholder(/Votre message/)
      .fill("Avec plaisir, nous venons à 9 h.");
    await owner.getByRole("button", { name: "Accepter avec ce devis" }).click();
    await expect(owner.getByText("Acceptée")).toBeVisible();

    await page.goto("/compte/fidelite");
    const request_ = page.locator("li").filter({ hasText: "Mariage" });
    await expect(request_).toContainText("Acceptée");
    await expect(request_).toContainText("180");
  });

  test("home recommends nearby salons and the dark theme applies everywhere without breaking the layout", async ({
    page,
  }) => {
    const client = await createAccount(request, "Client");

    await page.goto("/");
    const nearby = page.getByRole("region", {
      name: /Salons près de chez vous/,
    });
    await expect(nearby.locator(".reason").first()).toBeVisible();

    await signIn(page, client);
    await page.goto("/compte");
    await page.getByRole("radio", { name: "Sombre" }).check();
    await page.getByRole("radio", { name: "Grande", exact: true }).check();
    await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
    await expect(page.locator("html")).toHaveAttribute(
      "data-text-size",
      "large",
    );

    // Remembered on the device.
    await page.goto(`/etablissements/${salon.slug}`);
    await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
    const background = await page.evaluate(
      () => getComputedStyle(document.body).backgroundColor,
    );
    expect(background).not.toBe("rgb(255, 252, 248)");
    await expectNoHorizontalScroll(page);
  });
});
