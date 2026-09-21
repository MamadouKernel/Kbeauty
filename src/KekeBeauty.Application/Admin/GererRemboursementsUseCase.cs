using KekeBeauty.Application.Billing;
using KekeBeauty.Application.Rdv;

namespace KekeBeauty.Application.Admin;

public sealed class GererRemboursementsUseCase
{
    private readonly IRdvPaiementRepository _repository;
    private readonly IWaveMoneyGateway _wave;

    public GererRemboursementsUseCase(IRdvPaiementRepository repository, IWaveMoneyGateway wave)
    {
        _repository = repository;
        _wave = wave;
    }

    public Task<IReadOnlyList<DemandeRemboursementDto>> ListerAsync(CancellationToken cancellationToken) =>
        _repository.ListerDemandesRemboursementAsync(cancellationToken);

    public async Task<bool> TraiterAsync(Guid idRdv, string referenceRemboursement, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(referenceRemboursement)) return false;
        return await _repository.TraiterRemboursementAsync(idRdv, referenceRemboursement, cancellationToken);
    }

    public async Task<WaveOperationResult> ExecuterWaveAsync(Guid idRdv, CancellationToken cancellationToken)
    {
        var demande = await _repository.ObtenirRemboursementWaveAsync(idRdv, cancellationToken);
        if (demande is null) return new(false, "refund_not_found");
        if (!demande.ReferenceExterne.StartsWith("cos-", StringComparison.OrdinalIgnoreCase))
            return new(false, "unsupported_payment_provider", Error: "Ce paiement n'est pas un checkout Wave direct.");

        var result = await _wave.RefundCheckoutAsync(demande.ReferenceExterne, cancellationToken);
        await _repository.EnregistrerTentativeRemboursementAsync(idRdv, result.Success,
            result.Reference ?? demande.ReferenceExterne, result.Error, cancellationToken);
        return result;
    }
}