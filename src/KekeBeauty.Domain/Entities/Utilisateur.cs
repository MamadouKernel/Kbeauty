namespace KekeBeauty.Domain.Entities;

public enum TypeCompte
{
    Client,
    Partenaire,
    Admin,
}

public class Utilisateur
{
    public Guid IdUtilisateur { get; set; }
    public string Telephone { get; set; } = string.Empty;
    public string Nom { get; set; } = string.Empty;
    public TypeCompte TypeCompte { get; set; }
    public DateTimeOffset DateCreation { get; set; }
    public bool EstSuspendu { get; set; }
}
