namespace KekeBeauty.Domain.Entities;

public class Commune
{
    public Guid IdCommune { get; set; }
    public string LibelleCommune { get; set; } = string.Empty;
    public Guid IdVille { get; set; }
}
