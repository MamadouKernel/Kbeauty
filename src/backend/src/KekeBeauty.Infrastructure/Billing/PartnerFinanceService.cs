using System.Globalization;
using System.Text;
using KekeBeauty.Infrastructure.Admin;
using KekeBeauty.Infrastructure.Partner;
using KekeBeauty.Infrastructure.Persistence;
using KekeBeauty.Infrastructure.Scheduling;
using KekeBeauty.Modules.Billing.Domain;
using KekeBeauty.Modules.Scheduling.Domain;
using KekeBeauty.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace KekeBeauty.Infrastructure.Billing;

public sealed record PartnerServiceRevenueRow(string ServiceName, int Count, decimal TotalAmount);

public sealed record PartnerMethodBreakdown(string Method, int Count, decimal TotalAmount);

public sealed record PartnerFinanceTransactionRow(
    Guid AppointmentId,
    DateTimeOffset StartsAt,
    string ClientName,
    string? ResourceName,
    string ServicesSummary,
    string Status,
    decimal TotalPrice,
    decimal OnlinePaidAmount,
    decimal PlatformFee,
    decimal NetOnlineAmount,
    string? OnSitePaidMethod,
    string? OnSitePaidReference,
    decimal TipAmount,
    decimal CommissionAmount,
    string CurrencyCode);

public sealed record PartnerFinanceSummary(
    Guid EstablishmentId,
    string EstablishmentName,
    DateTimeOffset From,
    DateTimeOffset To,
    string CurrencyCode,
    int CompletedCount,
    int NoShowCount,
    decimal GrossRevenue,
    decimal OnlineGross,
    decimal PlatformFees,
    decimal OnlineNet,
    decimal OnSiteCash,
    decimal OnSiteMobileMoney,
    decimal TotalTips,
    decimal TotalCommissions,
    decimal TotalPaidOut,
    IReadOnlyList<PartnerServiceRevenueRow> ByService,
    IReadOnlyList<PartnerMethodBreakdown> ByMethod,
    IReadOnlyList<PartnerFinanceTransactionRow> Transactions,
    IReadOnlyList<PayoutDto> Payouts);

/// <summary>Partner accounting & treasury: KPIs, channel breakdown, CSV export and PDF receipts (Lot 5 #31 & #69).</summary>
public sealed class PartnerFinanceService(
    KekeBeautyDbContext db,
    TimeProvider time)
{
    public const int MaxRangeDays = 366;

    public async Task<Result<PartnerFinanceSummary>> SummaryAsync(
        Guid userId,
        Guid establishmentId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct)
    {
        var est = await PartnerSupport.OwnedBy(db, userId, establishmentId)
            .AsNoTracking()
            .Select(e => new { e.Id, e.Name, e.Slug })
            .FirstOrDefaultAsync(ct);
        if (est is null)
        {
            return Result.Failure<PartnerFinanceSummary>(PartnerErrors.NotFound);
        }

        var now = time.GetUtcNow();
        var end = to ?? now.AddDays(1);
        var start = from ?? now.AddDays(-30);
        if (end <= start || (end - start).TotalDays > MaxRangeDays)
        {
            return Result.Failure<PartnerFinanceSummary>(PartnerErrors.Invalid($"La période doit couvrir entre 1 et {MaxRangeDays} jours."));
        }

        var appointments = await db.Appointments.AsNoTracking()
            .Where(a => a.EstablishmentId == establishmentId && !a.AwaitingPayment && a.StartsAt >= start && a.StartsAt <= end)
            .OrderByDescending(a => a.StartsAt)
            .ToListAsync(ct);

        var appointmentIds = appointments.Select(a => a.Id).ToList();
        var lines = appointmentIds.Count == 0
            ? []
            : await db.AppointmentLines.AsNoTracking().Where(l => appointmentIds.Contains(l.AppointmentId)).ToListAsync(ct);

        var (transactions, byService, byMethod) = await BuildRowsAsync(establishmentId, appointments, lines, ct);

        var payouts = await db.Payouts.AsNoTracking()
            .Where(p => p.EstablishmentId == establishmentId && p.CreatedAt >= start && p.CreatedAt <= end)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PayoutDto(
                p.Id, p.EstablishmentId, est.Name, p.Amount, p.CurrencyCode, p.Operator,
                PhoneNumbers.Mask(p.Account), p.Status.ToString(), p.Error, p.CreatedAt, p.CompletedAt))
            .ToListAsync(ct);

        var paidRows = transactions.Where(t => t.Status == "Completed" || t.OnSitePaidMethod != null || t.OnlinePaidAmount > 0).ToList();
        return Result.Success(new PartnerFinanceSummary(
            est.Id,
            est.Name,
            start,
            end,
            "XOF",
            transactions.Count(t => t.Status == "Completed"),
            transactions.Count(t => t.Status == "NoShow"),
            paidRows.Sum(t => t.TotalPrice),
            transactions.Sum(t => t.OnlinePaidAmount),
            transactions.Sum(t => t.PlatformFee),
            transactions.Sum(t => t.NetOnlineAmount),
            transactions.Where(t => t.OnSitePaidMethod == "Cash").Sum(t => t.TotalPrice - t.OnlinePaidAmount),
            transactions.Where(t => t.OnSitePaidMethod != null && t.OnSitePaidMethod != "Cash").Sum(t => t.TotalPrice - t.OnlinePaidAmount),
            transactions.Sum(t => t.TipAmount),
            transactions.Sum(t => t.CommissionAmount),
            payouts.Where(p => p.Status == "Succeeded").Sum(p => p.Amount),
            byService,
            byMethod,
            transactions,
            payouts));
    }

    public async Task<Result<(byte[] Content, string FileName)>> ExportCsvAsync(
        Guid userId,
        Guid establishmentId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct)
    {
        var summaryResult = await SummaryAsync(userId, establishmentId, from, to, ct);
        if (summaryResult.IsFailure)
        {
            return Result.Failure<(byte[], string)>(summaryResult.Error);
        }

        var s = summaryResult.Value;
        var csv = new StringBuilder();
        csv.AppendLine("date_rdv;statut;cliente;prestations;collaboratrice;montant_prestation;encaisse_en_ligne;frais_plateforme;net_en_ligne;reglement_sur_place;reference;pourboire;commission_collaboratrice;devise");
        foreach (var t in s.Transactions)
        {
            csv.Append(string.Join(';',
            [
                AdminFinanceService.Cell(t.StartsAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
                AdminFinanceService.Cell(t.Status),
                AdminFinanceService.Cell(t.ClientName),
                AdminFinanceService.Cell(t.ServicesSummary),
                AdminFinanceService.Cell(t.ResourceName ?? string.Empty),
                t.TotalPrice.ToString("0.##", CultureInfo.InvariantCulture),
                t.OnlinePaidAmount.ToString("0.##", CultureInfo.InvariantCulture),
                t.PlatformFee.ToString("0.##", CultureInfo.InvariantCulture),
                t.NetOnlineAmount.ToString("0.##", CultureInfo.InvariantCulture),
                AdminFinanceService.Cell(t.OnSitePaidMethod ?? string.Empty),
                AdminFinanceService.Cell(t.OnSitePaidReference ?? string.Empty),
                t.TipAmount.ToString("0.##", CultureInfo.InvariantCulture),
                t.CommissionAmount.ToString("0.##", CultureInfo.InvariantCulture),
                AdminFinanceService.Cell(t.CurrencyCode),
            ])).Append("\r\n");
        }

        var fileName = $"comptabilite-{establishmentId.ToString("N")[..8]}-{s.From:yyyyMMdd}-{s.To:yyyyMMdd}.csv";
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return Result.Success((bytes, fileName));
    }

    public async Task<Result<(byte[] Content, string FileName)>> AppointmentReceiptPdfAsync(
        Guid userId,
        Guid appointmentId,
        CancellationToken ct)
    {
        var row = await (
            from appt in db.Appointments.AsNoTracking()
            where appt.Id == appointmentId
            join e in db.Establishments.AsNoTracking() on appt.EstablishmentId equals e.Id
            where e.OwnerUserId == userId
            select new { Appointment = appt, EstablishmentName = e.Name })
            .FirstOrDefaultAsync(ct);
        if (row is null)
        {
            return Result.Failure<(byte[], string)>(SchedulingErrors.AppointmentNotFound);
        }

        var a = row.Appointment;
        var clientName = a.WalkInName ?? (a.ClientUserId is { } clientId
            ? await db.Users.AsNoTracking().Where(u => u.Id == clientId).Select(u => (u.FirstName + " " + u.LastName).Trim()).FirstOrDefaultAsync(ct) ?? "Cliente"
            : "Cliente");
        var resourceName = await db.Resources.AsNoTracking().Where(r => r.Id == a.ResourceId).Select(r => r.Name).FirstOrDefaultAsync(ct) ?? "Salon";
        var lines = await db.AppointmentLines.AsNoTracking()
            .Where(l => l.AppointmentId == a.Id)
            .Select(l => new ValueTuple<string, decimal>(l.NameSnapshot, l.PriceSnapshot))
            .ToListAsync(ct);
        var online = await db.AppointmentPayments.AsNoTracking()
            .Where(p => p.AppointmentId == a.Id && p.Status == AppointmentPaymentStatus.Paid)
            .FirstOrDefaultAsync(ct);

        var methodLabel = online is not null
            ? "En ligne"
            : !string.IsNullOrWhiteSpace(a.ServicePaidMethod) ? MethodLabel(a.ServicePaidMethod) : "Sur place";
        var shortRef = $"KB-RDV-{a.Id.ToString("N")[..8].ToUpperInvariant()}";
        var pdf = InvoicePdf.BuildAppointmentReceipt(
            shortRef, row.EstablishmentName, clientName, resourceName, a.StartsAt, lines,
            a.TotalPrice, a.TipAmount, a.CurrencyCode, methodLabel, a.ServicePaidReference);
        return Result.Success((pdf, $"{shortRef}.pdf"));
    }

    public async Task<Result<(byte[] Content, string FileName)>> PayoutReceiptPdfAsync(
        Guid userId,
        Guid payoutId,
        CancellationToken ct)
    {
        var row = await (
            from pay in db.Payouts.AsNoTracking()
            where pay.Id == payoutId
            join e in db.Establishments.AsNoTracking() on pay.EstablishmentId equals e.Id
            where e.OwnerUserId == userId
            select new { Payout = pay, EstablishmentName = e.Name })
            .FirstOrDefaultAsync(ct);
        if (row is null)
        {
            return Result.Failure<(byte[], string)>(new Error("payout.not_found", "Reversement introuvable."));
        }

        var p = row.Payout;
        var dto = new PayoutDto(
            p.Id, p.EstablishmentId, row.EstablishmentName, p.Amount, p.CurrencyCode, p.Operator,
            PhoneNumbers.Mask(p.Account), p.Status.ToString(), p.Error, p.CreatedAt, p.CompletedAt);
        var shortRef = $"KB-PAY-{p.Id.ToString("N")[..8].ToUpperInvariant()}";
        return Result.Success((InvoicePdf.BuildPayoutReceipt(dto, row.EstablishmentName), $"{shortRef}.pdf"));
    }

    private async Task<(
        List<PartnerFinanceTransactionRow> Transactions,
        List<PartnerServiceRevenueRow> ByService,
        List<PartnerMethodBreakdown> ByMethod)> BuildRowsAsync(
        Guid establishmentId,
        IReadOnlyList<Appointment> appointments,
        IReadOnlyList<AppointmentLine> lines,
        CancellationToken ct)
    {
        if (appointments.Count == 0)
        {
            return ([], [], []);
        }

        var appointmentIds = appointments.Select(a => a.Id).ToList();
        var clientIds = appointments.Where(a => a.ClientUserId != null).Select(a => a.ClientUserId!.Value).Distinct().ToList();
        var clients = await db.Users.AsNoTracking().Where(u => clientIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);
        var resources = await db.Resources.AsNoTracking().Where(r => r.EstablishmentId == establishmentId)
            .ToDictionaryAsync(r => r.Id, r => (r.Name, r.CommissionPercent), ct);
        var linesByAppointment = lines.GroupBy(l => l.AppointmentId)
            .ToDictionary(g => g.Key, g => string.Join(" + ", g.Select(l => l.NameSnapshot)));
        var onlineByAppointment = (await db.AppointmentPayments.AsNoTracking()
                .Where(p => appointmentIds.Contains(p.AppointmentId) && p.Status == AppointmentPaymentStatus.Paid)
                .OrderByDescending(p => p.CreatedAt).ToListAsync(ct))
            .GroupBy(p => p.AppointmentId)
            .ToDictionary(g => g.Key, g => g.First());

        var transactions = appointments.Select(a =>
        {
            var finished = CommissionService.IsFinished(a);
            var (resName, rate) = resources.TryGetValue(a.ResourceId, out var r) ? r : ("Salon", 0m);
            var commission = finished ? (a.CommissionAmount ?? CommissionService.CommissionOf(a.TotalPrice, rate)) : 0m;
            var online = onlineByAppointment.GetValueOrDefault(a.Id);
            var onlinePaid = online?.Amount ?? 0m;
            var fee = online?.PlatformFee ?? 0m;
            var netOnline = online?.NetAmount ?? 0m;
            var clientName = a.WalkInName ?? (a.ClientUserId is { } cid && clients.TryGetValue(cid, out var cname) ? cname : "Cliente");
            return new PartnerFinanceTransactionRow(
                a.Id,
                a.StartsAt,
                clientName,
                resName,
                linesByAppointment.GetValueOrDefault(a.Id, "Prestation"),
                finished ? "Completed" : a.Status.ToString(),
                a.TotalPrice,
                onlinePaid,
                fee,
                netOnline,
                a.ServicePaidMethod,
                a.ServicePaidReference,
                a.TipAmount,
                commission,
                a.CurrencyCode);
        }).ToList();

        var completedIds = transactions.Where(t => t.Status == "Completed" || t.OnSitePaidMethod != null || t.OnlinePaidAmount > 0)
            .Select(t => t.AppointmentId).ToHashSet();
        var byService = lines.Where(l => completedIds.Contains(l.AppointmentId))
            .GroupBy(l => l.NameSnapshot)
            .Select(g => new PartnerServiceRevenueRow(g.Key, g.Count(), g.Sum(x => x.PriceSnapshot)))
            .OrderByDescending(x => x.TotalAmount)
            .ToList();

        var byMethod = transactions.Where(t => completedIds.Contains(t.AppointmentId))
            .GroupBy(t => t.OnlinePaidAmount > 0 ? "En ligne" : MethodLabel(t.OnSitePaidMethod ?? "Sur place"))
            .Select(g => new PartnerMethodBreakdown(g.Key, g.Count(), g.Sum(x => x.TotalPrice)))
            .OrderByDescending(x => x.TotalAmount)
            .ToList();

        return (transactions, byService, byMethod);
    }

    private static string MethodLabel(string method) => method switch
    {
        "Cash" => "Espèces sur place",
        "wave-cote-divoire" or "Wave" => "Wave",
        "orange-money-cote-divoire" or "OrangeMoney" => "Orange Money",
        "mtn-cote-divoire" or "Mtn" => "MTN MoMo",
        "moov-cote-divoire" or "Moov" => "Moov Money",
        _ => method,
    };
}
