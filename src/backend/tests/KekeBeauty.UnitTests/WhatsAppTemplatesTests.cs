using System.Text.RegularExpressions;
using FluentAssertions;
using KekeBeauty.Infrastructure.Notifications;
using KekeBeauty.Modules.Notifications.Domain;

namespace KekeBeauty.UnitTests;

public sealed partial class WhatsAppTemplatesTests
{
    private static readonly Dictionary<string, string> Variables = new()
    {
        ["etablissement"] = "Maison Éclat Coiffure",
        ["date"] = "12/11/2026",
        ["heure"] = "10:00",
        ["montant"] = "10 000 F CFA",
        ["client"] = "Awa Koné",
    };

    [Fact]
    public void An_appointment_notification_fills_its_template_in_body_order_with_the_first_name_from_the_account()
    {
        var built = WhatsAppTemplates.Build("appointment.confirmed", Variables, " Awa ");

        built.Should().NotBeNull();
        built!.Value.Template.Name.Should().Be("rdv_confirme");
        built.Value.Template.Purpose.Should().Be(ConsentPurpose.Service);
        built.Value.Parameters.Should().Equal("Awa", "Maison Éclat Coiffure", "12/11/2026", "10:00");
    }

    [Fact]
    public void The_payment_template_takes_the_amount_before_the_establishment()
    {
        WhatsAppTemplates.Build("appointment.payment_received", Variables, "Awa")!.Value.Parameters
            .Should().Equal("Awa", "10 000 F CFA", "Maison Éclat Coiffure");
    }

    [Fact]
    public void Only_the_invitation_to_come_back_is_marketing_and_it_needs_no_date()
    {
        WhatsAppTemplates.ByNotification.Where(t => t.Value.Purpose == ConsentPurpose.Marketing).Select(t => t.Key).Should().Equal("client.reengagement");

        var built = WhatsAppTemplates.Build("client.reengagement", new Dictionary<string, string> { ["etablissement"] = "Maison Éclat Coiffure" }, "Awa");

        built!.Value.Template.Name.Should().Be("keke_retour");
        built.Value.Parameters.Should().Equal("Awa", "Maison Éclat Coiffure");
    }

    [Theory]
    [InlineData("appointment.confirmed.partner")]
    [InlineData("appointment.client_late.partner")]
    [InlineData("billing.payment_failed")]
    [InlineData("partner.weekly_recap")]
    [InlineData("dispute.opened")]
    [InlineData("anything.else")]
    public void Partners_billing_and_unmapped_messages_never_go_out_by_whatsapp(string code)
    {
        WhatsAppTemplates.Build(code, Variables, "Awa").Should().BeNull();
    }

    [Fact]
    public void A_missing_blank_or_absent_variable_means_nothing_is_sent_rather_than_a_template_meta_refuses()
    {
        WhatsAppTemplates.Build("appointment.confirmed", null, "Awa").Should().BeNull();
        WhatsAppTemplates.Build("appointment.confirmed", Variables, null).Should().BeNull();
        WhatsAppTemplates.Build("appointment.confirmed", Variables, "  ").Should().BeNull();
        WhatsAppTemplates.Build("appointment.confirmed", new Dictionary<string, string>(Variables) { ["heure"] = " " }, "Awa").Should().BeNull();
        WhatsAppTemplates.Build("appointment.confirmed", Variables.Where(v => v.Key != "date").ToDictionary(v => v.Key, v => v.Value), "Awa").Should().BeNull();
    }

    // The dossier sent to the provider (docs/WHATSAPP-TEMPLATES.md) and the code must say the same thing, and the texts must follow Meta's rules.

    [GeneratedRegex(@"^\| `(?<name>[a-z_]+)` \| (?<body>[^|]*\{\{1\}\}[^|]*) \|\s*$", RegexOptions.Multiline)]
    private static partial Regex TemplateRow();

    private static string Dossier()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docs", "WHATSAPP-TEMPLATES.md")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the dossier lives in docs/");
        return File.ReadAllText(Path.Combine(dir!.FullName, "docs", "WHATSAPP-TEMPLATES.md"));
    }

    [Fact]
    public void Every_template_the_code_uses_is_written_in_the_dossier_with_the_same_number_of_variables()
    {
        var rows = TemplateRow().Matches(Dossier()).ToDictionary(m => m.Groups["name"].Value, m => m.Groups["body"].Value);

        foreach (var template in WhatsAppTemplates.ByNotification.Values.DistinctBy(t => t.Name))
        {
            rows.Should().ContainKey(template.Name);
            Regex.Matches(rows[template.Name], @"\{\{\d+\}\}").Count.Should().Be(template.Variables.Length, template.Name);
        }
    }

    [Fact]
    public void The_dossier_texts_follow_metas_rules_on_variables_and_length()
    {
        var rows = TemplateRow().Matches(Dossier()).Select(m => (Name: m.Groups["name"].Value, Body: m.Groups["body"].Value.Trim())).ToList();

        rows.Should().HaveCountGreaterThanOrEqualTo(10);
        foreach (var (name, body) in rows)
        {
            body.Should().NotStartWith("{{", name);
            body.TrimEnd().Should().NotEndWith("}}", name);
            Regex.IsMatch(body, @"\}\}\s*\{\{").Should().BeFalse($"{name} has two variables in a row");
            body.Length.Should().BeLessThanOrEqualTo(1024, name);
            Regex.IsMatch(name, "^[a-z][a-z0-9_]*$").Should().BeTrue(name);
            var numbers = Regex.Matches(body, @"\{\{(\d+)\}\}").Select(x => int.Parse(x.Groups[1].Value)).ToList();
            numbers.Distinct().Order().Should().Equal(Enumerable.Range(1, numbers.Max()), $"{name} numbers its variables in order");
        }
    }
}
