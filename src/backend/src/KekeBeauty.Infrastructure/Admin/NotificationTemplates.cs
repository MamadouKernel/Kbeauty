using System.Text.RegularExpressions;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Administration.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Admin;

public sealed record TemplateDefinition(string Code, string Label, IReadOnlyList<string> Variables, string DefaultTitle, string DefaultBody);

/// <summary>
/// Messages the admin may reword (PRD NOT-02). The default texts shown here mirror the ones built in the code;
/// a stored override replaces them whenever the sender supplies the variables.
/// </summary>
public static class NotificationTemplateCatalog
{
    private static readonly string[] Appointment = ["etablissement", "client", "date", "heure", "motif"];
    private static readonly string[] AppointmentPayment = ["etablissement", "client", "date", "heure", "motif", "montant"];
    private static readonly string[] Invoice = ["etablissement", "facture", "montant", "echeance", "jours"];

    public static readonly IReadOnlyList<TemplateDefinition> All =
    [
        new("appointment.requested", "Demande de rendez-vous envoyée (client)", Appointment, "Demande envoyée", "Votre demande chez {etablissement} le {date} à {heure} a bien été envoyée. Vous serez prévenu dès sa validation."),
        new("appointment.requested.partner", "Nouvelle demande de rendez-vous (partenaire)", Appointment, "Nouvelle demande de rendez-vous", "{client} souhaite réserver le {date} à {heure}. Répondez depuis votre agenda."),
        new("appointment.confirmed", "Rendez-vous confirmé (client)", Appointment, "Rendez-vous confirmé", "Votre rendez-vous chez {etablissement} le {date} à {heure} est confirmé."),
        new("appointment.confirmed.partner", "Nouveau rendez-vous confirmé (partenaire)", Appointment, "Nouveau rendez-vous", "{client} a réservé le {date} à {heure} (confirmation automatique)."),
        new("appointment.rejected", "Demande refusée (client)", Appointment, "Demande refusée", "{etablissement} ne peut pas vous recevoir le {date} à {heure}. Motif : {motif}"),
        new("appointment.expired", "Demande expirée (client)", Appointment, "Demande expirée", "{etablissement} n'a pas répondu à votre demande le {date} à {heure}. Le créneau est libéré."),
        new("appointment.expired.partner", "Demande expirée (partenaire)", Appointment, "Demande expirée", "La demande de {client} le {date} à {heure} a expiré sans réponse."),
        new("appointment.cancelled_by_establishment", "Rendez-vous annulé par l'établissement (client)", Appointment, "Rendez-vous annulé", "{etablissement} a annulé votre rendez-vous le {date} à {heure}. Motif : {motif}"),
        new("appointment.cancelled_by_client.partner", "Rendez-vous annulé par le client (partenaire)", Appointment, "Rendez-vous annulé", "{client} a annulé son rendez-vous le {date} à {heure}."),
        new("appointment.reschedule_proposed", "Nouveau créneau proposé (client)", Appointment, "Nouveau créneau proposé", "{etablissement} vous propose le {date} à {heure} à la place. Acceptez ou refusez dans Mes rendez-vous."),
        new("appointment.reschedule_accepted", "Reprogrammation acceptée (client)", Appointment, "Rendez-vous reprogrammé", "Votre rendez-vous chez {etablissement} est confirmé le {date} à {heure}."),
        new("appointment.reschedule_accepted.partner", "Reprogrammation acceptée (partenaire)", Appointment, "Reprogrammation acceptée", "{client} a accepté le nouveau créneau : le {date} à {heure}."),
        new("appointment.reminder", "Rappel avant le rendez-vous (client)", Appointment, "Rappel de rendez-vous", "Rappel : votre rendez-vous chez {etablissement} a lieu le {date} à {heure}."),
        new("appointment.payment_received", "Paiement du rendez-vous reçu (client)", AppointmentPayment, "Paiement reçu", "Votre paiement de {montant} pour le rendez-vous chez {etablissement} le {date} à {heure} a bien été reçu."),
        new("appointment.payment_failed", "Paiement du rendez-vous non abouti (client)", AppointmentPayment, "Paiement non abouti", "Le paiement de votre rendez-vous chez {etablissement} n'a pas abouti. Réessayez depuis Mes rendez-vous avant la fin du délai de réservation."),
        new("appointment.refunded", "Remboursement effectué (client)", AppointmentPayment, "Remboursement effectué", "Votre paiement de {montant} pour le rendez-vous chez {etablissement} vous a été remboursé."),
        new("billing.payout_sent", "Reversement envoyé (partenaire)", Invoice, "Reversement effectué", "Un reversement de {montant} pour « {etablissement} » a été envoyé sur votre compte de paiement mobile."),
        new("billing.payment_succeeded", "Paiement reçu", Invoice, "Paiement reçu", "Votre paiement de {montant} pour « {etablissement} » (facture {facture}) a bien été reçu. Merci !"),
        new("billing.payment_failed", "Paiement non abouti", Invoice, "Paiement non abouti", "Le paiement de la facture {facture} pour « {etablissement} » n'a pas abouti. Vous pouvez réessayer depuis votre espace abonnement."),
        new("billing.reminder.-3", "Rappel avant échéance (J-3)", Invoice, "Facture à venir", "La facture {facture} ({montant}) arrive à échéance le {echeance}."),
        new("billing.reminder.0", "Rappel le jour de l'échéance (J0)", Invoice, "Facture à régler aujourd'hui", "La facture {facture} ({montant}) est à régler aujourd'hui."),
        new("billing.reminder.3", "Rappel de retard (J+3)", Invoice, "Facture en retard", "La facture {facture} est en retard de {jours} jours. Réglez-la pour conserver vos fonctionnalités Premium."),
        new("billing.reminder.7", "Dernier rappel (J+7)", Invoice, "Dernier rappel", "La facture {facture} est impayée depuis {jours} jours : vos fonctionnalités Premium sont suspendues tant qu'elle n'est pas réglée."),
        new("billing.past_due", "Abonnement suspendu pour impayé", Invoice, "Abonnement suspendu pour impayé", "La facture {facture} n'est pas réglée : les fonctionnalités Premium de « {etablissement} » sont suspendues. Régularisez depuis votre espace abonnement."),
        new("billing.trial_ended", "Essai gratuit terminé", Invoice, "Essai gratuit terminé", "Votre essai gratuit pour « {etablissement} » est terminé : votre fiche reste visible avec la formule Gratuite. Souscrivez depuis votre espace abonnement pour retrouver toutes les fonctionnalités."),
        new("billing.plan_changed", "Changement de formule effectif", Invoice, "Changement de formule", "L'abonnement de « {etablissement} » est passé à sa nouvelle formule. Les nouvelles conditions s'appliquent dès maintenant."),
        new("billing.ended", "Abonnement terminé", Invoice, "Abonnement terminé", "Votre abonnement pour « {etablissement} » est arrivé au terme de son engagement et ne sera pas renouvelé."),
    ];

    public static TemplateDefinition? Find(string code) => All.FirstOrDefault(t => t.Code == code);
}

public static partial class TemplateRenderer
{
    [GeneratedRegex(@"\{([a-z_]+)\}")]
    private static partial Regex Placeholder();

    /// <summary>Replaces <c>{name}</c> by its value; a variable the sender did not provide becomes empty.</summary>
    public static string Render(string text, IReadOnlyDictionary<string, string> variables) =>
        Placeholder().Replace(text, m => variables.GetValueOrDefault(m.Groups[1].Value, string.Empty)).Trim();

    public static IReadOnlyList<string> Variables(string text) =>
        Placeholder().Matches(text).Select(m => m.Groups[1].Value).Distinct().ToList();

    public static bool HasStrayBraces(string text) => Placeholder().Replace(text, string.Empty).AsSpan().IndexOfAny('{', '}') >= 0;
}

public sealed record TemplateDto(
    string Code,
    string Label,
    IReadOnlyList<string> Variables,
    string DefaultTitle,
    string DefaultBody,
    string Title,
    string Body,
    bool IsCustom);

public sealed record TemplateInput(string? Title, string? Body);

public sealed class NotificationTemplateService(KekeBeautyDbContext db, TimeProvider time)
{
    public static readonly Error NotFound = new("template.not_found", "Modèle de message introuvable.");

    public async Task<IReadOnlyList<TemplateDto>> ListAsync(CancellationToken ct)
    {
        var overrides = await db.NotificationTemplates.AsNoTracking().ToDictionaryAsync(t => t.Code, ct);
        return NotificationTemplateCatalog.All.Select(d =>
        {
            var custom = overrides.GetValueOrDefault(d.Code);
            return new TemplateDto(d.Code, d.Label, d.Variables, d.DefaultTitle, d.DefaultBody, custom?.Title ?? d.DefaultTitle, custom?.Body ?? d.DefaultBody, custom is not null);
        }).ToList();
    }

    public async Task<Result> UpdateAsync(AdminContext admin, string code, TemplateInput input, CancellationToken ct)
    {
        var definition = NotificationTemplateCatalog.Find(code);
        if (definition is null)
        {
            return Result.Failure(NotFound);
        }

        var title = input.Title?.Trim() ?? string.Empty;
        var body = input.Body?.Trim() ?? string.Empty;
        if (title.Length is 0 or > 120 || body.Length is 0 or > 480)
        {
            return Result.Failure(AdminErrors.Invalid("Le titre (120 caractères) et le message (480 caractères) sont obligatoires."));
        }

        var unknown = TemplateRenderer.Variables(title).Concat(TemplateRenderer.Variables(body)).Except(definition.Variables).ToList();
        if (unknown.Count > 0 || TemplateRenderer.HasStrayBraces(title) || TemplateRenderer.HasStrayBraces(body))
        {
            var allowed = string.Join(", ", definition.Variables.Select(v => "{" + v + "}"));
            return Result.Failure(AdminErrors.Invalid($"Variables inconnues ou accolades mal formées. Variables disponibles : {allowed}."));
        }

        var now = time.GetUtcNow();
        var row = await db.NotificationTemplates.FirstOrDefaultAsync(t => t.Code == code, ct);
        if (row is null)
        {
            db.NotificationTemplates.Add(new NotificationTemplate { Code = code, Title = title, Body = body, UpdatedAt = now, UpdatedBy = admin.ActorId });
        }
        else
        {
            row.Title = title;
            row.Body = body;
            row.UpdatedAt = now;
            row.UpdatedBy = admin.ActorId;
        }

        Audit.Add(db, admin, time, "template.update", "Template", Guid.Empty, new { code });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> ResetAsync(AdminContext admin, string code, CancellationToken ct)
    {
        if (NotificationTemplateCatalog.Find(code) is null)
        {
            return Result.Failure(NotFound);
        }

        await db.NotificationTemplates.Where(t => t.Code == code).ExecuteDeleteAsync(ct);
        Audit.Add(db, admin, time, "template.reset", "Template", Guid.Empty, new { code });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
