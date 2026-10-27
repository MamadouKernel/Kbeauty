using KekeBeauty.Modules.Notifications.Domain;

namespace KekeBeauty.Infrastructure.Notifications;

/// <summary>
/// One WhatsApp template per message (docs/WHATSAPP-TEMPLATES.md): which approved Meta template carries which notification, with its
/// variables in the order of the template body. A notification without a template here never goes out by WhatsApp (partners and billing
/// are told by e-mail), and a marketing template is only used with the person's separate marketing agreement.
/// </summary>
public static class WhatsAppTemplates
{
    /// <summary>The variable that stands for the person's first name (taken from the account, not from the notification).</summary>
    public const string FirstName = "prenom";

    public sealed record Template(string Name, ConsentPurpose Purpose, string[] Variables);

    private static readonly string[] Appointment = [FirstName, "etablissement", "date", "heure"];

    public static readonly IReadOnlyDictionary<string, Template> ByNotification = new Dictionary<string, Template>(StringComparer.Ordinal)
    {
        ["appointment.requested"] = new("rdv_demande_envoyee", ConsentPurpose.Service, Appointment),
        ["appointment.confirmed"] = new("rdv_confirme", ConsentPurpose.Service, Appointment),
        ["appointment.rejected"] = new("rdv_non_retenu", ConsentPurpose.Service, Appointment),
        ["appointment.expired"] = new("rdv_non_retenu", ConsentPurpose.Service, Appointment),
        ["appointment.cancelled_by_establishment"] = new("rdv_annule", ConsentPurpose.Service, Appointment),
        ["appointment.reschedule_proposed"] = new("rdv_nouveau_creneau", ConsentPurpose.Service, Appointment),
        ["appointment.rescheduled_by_client"] = new("rdv_deplace", ConsentPurpose.Service, Appointment),
        ["appointment.reschedule_accepted"] = new("rdv_deplace", ConsentPurpose.Service, Appointment),
        ["appointment.reminder"] = new("rdv_rappel", ConsentPurpose.Service, Appointment),
        ["waitlist.slot_freed"] = new("creneau_libere", ConsentPurpose.Service, Appointment),
        ["appointment.payment_received"] = new("paiement_recu", ConsentPurpose.Service, [FirstName, "montant", "etablissement"]),
        ["client.reengagement"] = new("keke_retour", ConsentPurpose.Marketing, [FirstName, "etablissement"]),
    };

    /// <summary>
    /// The template and its body parameters for a notification, or <c>null</c> when it has no WhatsApp template or a variable is
    /// missing or empty (a template with a blank variable would be refused by Meta, so nothing is sent).
    /// </summary>
    public static (Template Template, IReadOnlyList<string> Parameters)? Build(string notificationCode, IReadOnlyDictionary<string, string>? variables, string? firstName)
    {
        if (variables is null || !ByNotification.TryGetValue(notificationCode, out var template))
        {
            return null;
        }

        var values = new List<string>(template.Variables.Length);
        foreach (var name in template.Variables)
        {
            var value = name == FirstName ? firstName : variables.GetValueOrDefault(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            values.Add(value.Trim());
        }

        return (template, values);
    }
}
