using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Billing;
using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Infrastructure.Search;

namespace KekeBeauty.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class Lot5FinancesAndFormulasTests(ApiFixture api)
{
    [Fact]
    public async Task Admin_can_create_and_delete_unused_plan_with_max_services_and_perks_and_read_analytical_dashboard()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var features = new PlanFeaturesDto(true, true, 20, true, true, 3, 5, 100, MaxServices: 2, Perks: ["Support VIP WhatsApp", "Formation équipe"]);
        var create = await admin.Client.PostAsJsonAsync(
            "/api/v1/admin/plans",
            new PlanUpdate($"Formule Or {Guid.NewGuid():N}"[..18], 10000, 100000, features, true, "Formule complète", "Or", 6));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var plan = (await create.Content.ReadFromJsonAsync<PlanDto>())!;
        plan.Features.MaxServices.Should().Be(2);
        plan.Features.Perks.Should().Equal("Support VIP WhatsApp", "Formation équipe");

        // Analytical dashboard returns global KPIs.
        var dashboard = await admin.Client.GetFromJsonAsync<AdminDashboardDto>("/api/v1/admin/dashboard");
        dashboard.Should().NotBeNull();
        dashboard!.CurrencyCode.Should().Be("XOF");
        dashboard.TotalUsers.Should().BeGreaterThan(0);

        //Unused plan can be deleted.
        var del = await admin.Client.DeleteAsync($"/api/v1/admin/plans/{plan.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Salon_service_promos_and_perks_apply_to_bookings_and_partner_finances_export_and_receipt_work()
    {
        using var admin = await TestAccounts.AdminAsync(api);
        var salon = await TestSalons.PublishAsync(api, admin, subscribe: true, autoConfirm: true);
        using var client = await TestAccounts.ClientAsync(api);
        var baseUrl = $"/api/v1/pro/establishments/{salon.Id}";

        try
        {
            var detail = (await salon.Owner.Client.GetFromJsonAsync<PartnerEstablishmentDetail>(baseUrl))!;
            var categoryId = detail.CategoryIds[0];

            // Update the service with a 20% promotion and formula perks.
            var updateSvc = await salon.Owner.Client.PutAsJsonAsync(
                $"{baseUrl}/services/{salon.ServiceHalfHour}",
                new ServiceInput(
                    "Forfait Éclat Royal",
                    "Soin complet visage et cheveux",
                    10000,
                    "Fixed",
                    30,
                    categoryId,
                    null,
                    true,
                    true,
                    PromoPercent: 20,
                    PromoEndsAt: DateTimeOffset.UtcNow.AddDays(10),
                    Perks: ["Shampoing kératine", "Massage crânien"]));
            updateSvc.StatusCode.Should().Be(HttpStatusCode.NoContent);

            // Public establishment and booking info expose the discounted price (8000 XOF) and perks.
            using var visitor = api.CreateClient();
            var pub = (await visitor.GetFromJsonAsync<EstablishmentDetails>($"/api/v1/establishments/{salon.Slug}"))!;
            var pubSvc = pub.ServiceGroups.SelectMany(g => g.Services).Single(s => s.Id == salon.ServiceHalfHour);
            pubSvc.PromoPercent.Should().Be(20);
            pubSvc.EffectivePrice.Should().Be(8000);
            pubSvc.Perks.Should().Equal("Shampoing kératine", "Massage crânien");

            var bookingInfo = (await visitor.GetFromJsonAsync<BookingInfo>($"/api/v1/establishments/{salon.Slug}/booking"))!;
            var bookSvc = bookingInfo.Services.Single(s => s.Id == salon.ServiceHalfHour);
            bookSvc.Price.Should().Be(8000);
            bookSvc.OriginalPrice.Should().Be(10000);

            // Client books the promoted formula -> TotalPrice is 8000 XOF.
            var day = TestSalons.NextOpenDay(api, 3);
            var booked = await client.Client.PostAsJsonAsync(
                "/api/v1/appointments",
                new BookingRequest(salon.Id, [salon.ServiceHalfHour], null, day, "07 00 00 12 34", null));
            booked.StatusCode.Should().Be(HttpStatusCode.Created);
            var appt = (await booked.Content.ReadFromJsonAsync<AppointmentDto>())!;
            appt.TotalPrice.Should().Be(8000);

            // Partner marks appointment paid in cash and completed.
            (await salon.Owner.Client.PostAsJsonAsync($"/api/v1/pro/appointments/{appt.Id}/mark-paid", new { method = "Cash", reference = (string?)null }))
                .EnsureSuccessStatusCode();

            // Partner fetches detailed finances, CSV export, and PDF receipt.
            var finances = (await salon.Owner.Client.GetFromJsonAsync<PartnerFinanceSummary>(
                $"{baseUrl}/finances?from={Uri.EscapeDataString(day.AddDays(-5).ToString("O"))}&to={Uri.EscapeDataString(day.AddDays(5).ToString("O"))}"))!;
            finances.EstablishmentId.Should().Be(salon.Id);
            finances.Transactions.Should().Contain(t => t.AppointmentId == appt.Id && t.TotalPrice == 8000);

            var csvResp = await salon.Owner.Client.GetAsync(
                $"{baseUrl}/finances.csv?from={Uri.EscapeDataString(day.AddDays(-5).ToString("O"))}&to={Uri.EscapeDataString(day.AddDays(5).ToString("O"))}");
            csvResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var csv = await csvResp.Content.ReadAsStringAsync();
            csv.Should().Contain("Forfait Éclat Royal").And.Contain("8000");

            var pdfResp = await salon.Owner.Client.GetAsync($"/api/v1/pro/appointments/{appt.Id}/receipt.pdf");
            pdfResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var pdfBytes = await pdfResp.Content.ReadAsByteArrayAsync();
            System.Text.Encoding.Latin1.GetString(pdfBytes).Should().StartWith("%PDF-1.4");
        }
        finally
        {
            await TestCleanup.DeleteEstablishmentsAsync(api, salon.Owner.UserId);
        }
    }
}
