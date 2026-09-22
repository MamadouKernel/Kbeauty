using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using KekeBeauty.Domain.Entities;

namespace KekeBeauty.Application.Auth;

public sealed record RegisterWithPasswordResult(bool Success, string Status, Guid? IdUtilisateur = null, bool EmailSent = false);

/// <summary>
/// Inscription par email + mot de passe, additive aux flux OTP telephone et Google existants
/// (voir echange utilisateur : ne remplace rien, un meme Utilisateur peut plus tard se connecter
/// par n'importe lequel des trois moyens). Le telephone reste obligatoire et unique par type de
/// compte, comme pour les autres flux d'inscription.
/// </summary>
public sealed partial class RegisterWithPasswordUseCase
{
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(30);

    private readonly IUtilisateurRepository _utilisateurRepository;
    private readonly IEmailSender _emailSender;

    public RegisterWithPasswordUseCase(IUtilisateurRepository utilisateurRepository, IEmailSender emailSender)
    {
        _utilisateurRepository = utilisateurRepository;
        _emailSender = emailSender;
    }

    public async Task<RegisterWithPasswordResult> ExecuteAsync(
        string nom, string telephone, string email, string password, TypeCompte typeCompte, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(nom))
        {
            return new RegisterWithPasswordResult(false, "invalid_nom");
        }

        if (!E164Regex().IsMatch(telephone))
        {
            return new RegisterWithPasswordResult(false, "invalid_phone");
        }

        if (!EmailRegex().IsMatch(email))
        {
            return new RegisterWithPasswordResult(false, "invalid_email");
        }

        if (password.Length < 8)
        {
            return new RegisterWithPasswordResult(false, "weak_password");
        }

        if (await _utilisateurRepository.FindByEmailAsync(email, typeCompte, cancellationToken) is not null)
        {
            return new RegisterWithPasswordResult(false, "email_already_used");
        }

        // Le telephone est deja utilise, que ce soit par un compte OTP/Google (password_hash NULL)
        // ou un autre compte mot de passe : on ne fusionne pas silencieusement, on redirige vers la
        // connexion existante (OTP/Google) plutot que de risquer une prise de compte par email.
        if (await _utilisateurRepository.FindByTelephoneAsync(telephone, typeCompte, cancellationToken) is not null)
        {
            return new RegisterWithPasswordResult(false, "telephone_already_used");
        }

        var passwordHash = PasswordHasher.Hash(password);
        var idUtilisateur = await _utilisateurRepository.CreateWithPasswordAsync(
            nom.Trim()[..Math.Min(nom.Trim().Length, 100)], telephone, email, passwordHash, typeCompte, cancellationToken);

        var code = GenerateSixDigitCode();
        await _utilisateurRepository.StorePasswordAuthTokenAsync(
            idUtilisateur, HashCode(code), "VERIFY_EMAIL", DateTimeOffset.UtcNow.Add(CodeLifetime), cancellationToken);

        var emailSent = await _emailSender.SendAsync(
            email, "Keke Beauty — Vérifiez votre adresse email",
            $"Bienvenue sur Keke Beauty ! Votre code de vérification est : {code}. Il expire dans 30 minutes.",
            cancellationToken);

        return new RegisterWithPasswordResult(true, "registered", idUtilisateur, emailSent);
    }

    private static string GenerateSixDigitCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();

    [GeneratedRegex(@"^\+[1-9]\d{7,14}$")]
    private static partial Regex E164Regex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();
}
