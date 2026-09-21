namespace KekeBeauty.Api.Auth;

public static class AdminPasswordPolicy
{
    public static bool IsValid(string? password) =>
        !string.IsNullOrWhiteSpace(password) && password.Length >= 12 &&
        password.Any(char.IsUpper) && password.Any(char.IsLower) &&
        password.Any(char.IsDigit) && password.Any(ch => !char.IsLetterOrDigit(ch));
}