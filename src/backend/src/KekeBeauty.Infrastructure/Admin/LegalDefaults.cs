namespace KekeBeauty.Infrastructure.Admin;

/// <summary>
/// Starting legal texts, seeded once and then maintained by the admins in the back-office. They are drafts written for the
/// services Keke Beauty provides and must be reviewed by legal counsel before going live (docs/VALIDATION-JURIDIQUE.md).
/// </summary>
public static class LegalDefaults
{
    public const string Terms = "cgu";
    public const string Privacy = "confidentialite";
    public const string LegalNotice = "mentions-legales";

    public static readonly IReadOnlyList<string> Slugs = [Terms, Privacy, LegalNotice];

    public sealed record Seed(string Slug, string Title, string Body, bool Published);

    public static readonly IReadOnlyList<Seed> All =
    [
        new(Terms, "Conditions d'utilisation", TermsBody, true),
        new(Privacy, "Politique de confidentialité", PrivacyBody, true),
        new(LegalNotice, "Mentions légales", LegalNoticeBody, false),
    ];

    private const string TermsBody = """
        ## 1. Objet

        Keke Beauty est un annuaire géolocalisé des salons de coiffure, spas, espaces massage et ongleries d'Abidjan, avec un service de demande de rendez-vous en ligne. Keke Beauty met en relation les clients et les établissements ; la prestation est conclue directement entre eux.

        ## 2. Comptes

        L'inscription est gratuite et réservée aux personnes majeures. Vous devez fournir des informations exactes et protéger votre mot de passe. Les comptes professionnels sont soumis à une vérification d'identité avant la publication d'une fiche. Keke Beauty peut suspendre un compte en cas d'abus, avec un motif communiqué.

        ## 3. Rendez-vous et paiement

        Une demande de rendez-vous n'est confirmée que lorsque l'établissement l'accepte, ou automatiquement si celui-ci a choisi la confirmation automatique. Le client peut annuler dans le délai fixé par l'établissement. Les prix affichés sont indicatifs.

        Lorsque l'établissement demande un acompte ou un paiement en ligne, le montant demandé est indiqué avant la validation et le rendez-vous n'est confirmé qu'après réception du paiement. En cas d'annulation dans le délai prévu par l'établissement, ou de refus par l'établissement, le montant payé est remboursé.

        ## 4. Professionnels : abonnement

        - La fiche est gratuite ; la réservation en ligne, les vidéos, les statistiques et la mise en avant relèvent de la formule Premium.
        - L'abonnement est souscrit pour 12 mois minimum, payable mensuellement ou annuellement par Mobile Money ou carte.
        - Le tarif d'un abonnement en cours ne change pas pendant l'engagement.
        - En cas d'impayé, après un délai de grâce, les fonctionnalités Premium sont suspendues ; la fiche reste visible.
        - La résiliation prend effet à la fin de l'engagement.

        ## 5. Contenus

        Les professionnels sont responsables des informations, photos, vidéos et tarifs qu'ils publient et garantissent détenir les droits nécessaires. Keke Beauty peut retirer un contenu non conforme.

        ## 6. Responsabilité

        Keke Beauty s'efforce d'assurer la disponibilité du service sans pouvoir la garantir. Elle n'est pas partie aux prestations de beauté et ne répond pas de leur qualité.

        ## 7. Droit applicable

        Les présentes conditions sont soumises au droit ivoirien.
        """;

    private const string PrivacyBody = """
        Keke Beauty traite vos données personnelles conformément à la loi ivoirienne n° 2013-450 relative à la protection des données à caractère personnel, sous le contrôle de l'ARTCI.

        ## Données collectées

        - Compte : nom, prénom, adresse e-mail, mot de passe (conservé sous forme chiffrée), préférences de double authentification.
        - Rendez-vous : établissement, date, prestations, numéro de téléphone et note éventuelle.
        - Professionnels : coordonnées de l'établissement, pièce d'identité du gérant (traitée de manière confidentielle et accessible aux seuls administrateurs habilités, chaque consultation étant journalisée) et informations de facturation.
        - Données techniques : adresse IP et appareil des sessions, pour la sécurité du compte.
        - Notifications : si vous les activez, l'abonnement de votre navigateur aux notifications et votre numéro de téléphone pour les SMS ou WhatsApp.

        ## Finalités

        Gérer votre compte, permettre les rendez-vous, vérifier les professionnels, facturer les abonnements, vous envoyer les notifications et rappels liés à vos rendez-vous et protéger le service contre la fraude. Votre localisation n'est utilisée que pour classer les établissements par distance, avec votre accord, et n'est pas conservée.

        ## Destinataires

        Les établissements reçoivent les informations nécessaires à vos rendez-vous. Nos prestataires de paiement, d'envoi de messages (e-mail, SMS, WhatsApp) et d'hébergement ne reçoivent que ce qui est indispensable à leur service. Vos données ne sont pas vendues.

        ## Cartes et itinéraires

        Les cartes sont fournies par un service de cartographie tiers : en affichant une carte, votre navigateur lui transmet votre adresse IP et la zone consultée. Les boutons d'itinéraire ouvrent Yango, Google Maps ou Plans (Apple), qui appliquent leurs propres politiques de confidentialité ; aucune donnée n'est transmise avant votre clic.

        ## Durées de conservation

        Les données du compte sont conservées tant qu'il est actif. À la suppression du compte, les informations personnelles sont effacées et les rendez-vous passés sont conservés sans données personnelles. Les images de la pièce d'identité d'un professionnel sont supprimées à l'issue d'une durée fixée par l'administration après la décision de vérification. Les données de facturation sont conservées pendant la durée exigée par la loi.

        ## Vos droits

        Vous pouvez à tout moment accéder à vos données, les rectifier, vous opposer à leur traitement et demander leur suppression. Depuis la page Mon compte, vous pouvez télécharger vos données et supprimer votre compte. Vous pouvez aussi saisir l'ARTCI.

        ## Cookies

        Keke Beauty n'utilise aucun cookie publicitaire ni de mesure d'audience. Un seul cookie technique, strictement nécessaire, maintient votre session connectée.
        """;

    private const string LegalNoticeBody = """
        ## Éditeur du site

        - Dénomination sociale : [à compléter]
        - Forme juridique et capital : [à compléter]
        - Siège social : [à compléter]
        - Registre du commerce (RCCM) : [à compléter]
        - Directeur de la publication : [à compléter]

        ## Contact

        - Adresse e-mail : [à compléter]
        - Téléphone : [à compléter]

        ## Hébergeur

        - Nom et adresse de l'hébergeur : [à compléter]

        ## Protection des données

        - Déclaration ou autorisation ARTCI n° : [à compléter]
        - Pour exercer vos droits : [à compléter]
        """;
}
