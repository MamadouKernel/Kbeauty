namespace KekeBeauty.Domain.Entities;

public enum TypeCompte
{
    Client,
    Partenaire,
    Admin,
    Collaborateur,
}

public class Utilisateur
{
    public Guid IdUtilisateur { get; set; }
    public string Telephone { get; set; } = string.Empty;
    public string Nom { get; set; } = string.Empty;
    public TypeCompte TypeCompte { get; set; }
    public DateTimeOffset DateCreation { get; set; }
    public bool EstSuspendu { get; set; }
    public string? Email { get; set; }
    public bool NotificationsRdv { get; set; }
    public bool NotificationsMarketing { get; set; }
    public bool ConsentementDonnees { get; set; }
    public DateTimeOffset? DateSuppression { get; set; }
    public string? PasswordHash { get; set; }
    public bool EmailVerifie { get; set; }
}

