using KekeBeauty.Infrastructure.Billing;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Modules.Catalog.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Partner;

public sealed record MobileMoneyInput(string? Operator, string? Account, string? HolderName);

public sealed record AcceptedPaymentsInput(bool Cash, IReadOnlyList<MobileMoneyInput>? MobileMoney);

public sealed record AcceptedPaymentsDto(bool Cash, IReadOnlyList<MobileMoneyDto> MobileMoney);

public sealed record MobileMoneyDto(string Operator, string Account, string HolderName);

/// <summary>
/// How the clients of an establishment pay it for the service: cash and/or mobile money straight to the accounts it declares.
/// The platform never touches this money; only the establishment's subscription goes through the payment provider.
/// </summary>
public sealed class AcceptedPaymentsService(KekeBeautyDbContext db)
{
    public const int MaxMobileMoneyAccounts = 4;

    public async Task<Result<AcceptedPaymentsDto>> GetAsync(Guid userId, Guid establishmentId, CancellationToken ct)
    {
        var e = await PartnerSupport.OwnedBy(db, userId, establishmentId).AsNoTracking().FirstOrDefaultAsync(ct);
        return e is null ? Result.Failure<AcceptedPaymentsDto>(PartnerErrors.NotFound) : Result.Success(ToDto(e));
    }

    public async Task<Result<AcceptedPaymentsDto>> UpdateAsync(Guid userId, Guid establishmentId, AcceptedPaymentsInput input, CancellationToken ct)
    {
        var e = await PartnerSupport.OwnedBy(db, userId, establishmentId).FirstOrDefaultAsync(ct);
        if (e is null)
        {
            return Result.Failure<AcceptedPaymentsDto>(PartnerErrors.NotFound);
        }

        var accounts = new List<MobileMoneyAccount>();
        foreach (var item in input.MobileMoney ?? [])
        {
            var account = PhoneNumbers.ToE164(item.Account);
            var holder = item.HolderName?.Trim();
            if (!PayoutOperators.IsKnown(item.Operator) || account is null || holder is not { Length: >= 2 and <= 100 })
            {
                return Result.Failure<AcceptedPaymentsDto>(PartnerErrors.Invalid("Pour chaque compte Mobile Money, indiquez l'opérateur, un numéro valide et le nom du titulaire."));
            }

            if (accounts.Any(a => a.Operator == item.Operator))
            {
                return Result.Failure<AcceptedPaymentsDto>(PartnerErrors.Invalid("Un seul compte par opérateur."));
            }

            accounts.Add(new MobileMoneyAccount { Operator = item.Operator!, Account = account, HolderName = holder });
        }

        if (accounts.Count > MaxMobileMoneyAccounts)
        {
            return Result.Failure<AcceptedPaymentsDto>(PartnerErrors.Invalid($"{MaxMobileMoneyAccounts} comptes Mobile Money au maximum."));
        }

        if (!input.Cash && accounts.Count == 0)
        {
            return Result.Failure<AcceptedPaymentsDto>(PartnerErrors.Invalid("Acceptez au moins un moyen de paiement : espèces ou Mobile Money."));
        }

        e.AcceptsCash = input.Cash;
        e.MobileMoney = accounts;
        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(e));
    }

    private static AcceptedPaymentsDto ToDto(Establishment e) =>
        new(e.AcceptsCash, e.MobileMoney.Select(m => new MobileMoneyDto(m.Operator, m.Account, m.HolderName)).ToList());
}
