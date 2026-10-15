using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Catalog.Domain;
using KekeBeauty.Modules.Onboarding.Domain;
using KekeBeauty.SharedKernel;

namespace KekeBeauty.Infrastructure.Partner;

/// <summary>Shared error codes and rules of the partner area.</summary>
public static class PartnerErrors
{
    public static readonly Error NotFound = new("establishment.not_found", "Établissement introuvable.");
    public static readonly Error Locked = new("establishment.locked", "Cet établissement ne peut plus être modifié dans son état actuel.");
    public static readonly Error Incomplete = new("establishment.incomplete", "Le dossier est incomplet : complétez les étapes manquantes avant de le soumettre.");

    public static Error Invalid(string message) => new("validation.failed", message);
}

public static class PartnerSupport
{
    /// <summary>Establishments whose content the owner may still change (a pending or refused one is frozen).</summary>
    public static bool IsEditable(EstablishmentStatus status) => status is EstablishmentStatus.Draft or EstablishmentStatus.Active;

    public static IQueryable<Establishment> OwnedBy(KekeBeautyDbContext db, Guid userId, Guid establishmentId) =>
        db.Establishments.Where(e => e.Id == establishmentId && e.OwnerUserId == userId);

    /// <summary>Items the owner still has to provide before the file can be submitted (PRD ONB-01 acceptance criteria).</summary>
    public static List<MissingItem> ComputeMissing(int categoryCount, int hourCount, int photoCount, KycIdentity? identity)
    {
        var missing = new List<MissingItem>();
        if (categoryCount == 0)
        {
            missing.Add(new MissingItem("category", "Choisir au moins une catégorie."));
        }

        if (hourCount == 0)
        {
            missing.Add(new MissingItem("hours", "Renseigner les horaires d'ouverture."));
        }

        if (photoCount == 0)
        {
            missing.Add(new MissingItem("photo", "Ajouter une photo de l'établissement."));
        }

        if (identity?.Status != KycStatus.Approved)
        {
            if (identity is null || string.IsNullOrWhiteSpace(identity.HolderName) || string.IsNullOrWhiteSpace(identity.DocumentNumber))
            {
                missing.Add(new MissingItem("identity", "Renseigner la pièce d'identité du gérant."));
            }

            if (identity is null || !identity.HasRequiredDocuments)
            {
                missing.Add(new MissingItem("identity_documents", "Téléverser le recto et le verso de la pièce d'identité."));
            }
        }

        return missing;
    }
}
