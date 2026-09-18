namespace KekeBeauty.Application.Auth;

public sealed record RequestOtpResult(bool Success, string Status, string? Message = null);

public sealed record VerifyOtpResult(bool Success, string Status, Guid? IdUtilisateur = null, bool IsNewAccount = false);
