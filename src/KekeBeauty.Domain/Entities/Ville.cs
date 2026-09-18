namespace KekeBeauty.Domain.Entities;

public class Ville
{
    public Guid IdVille { get; set; }
    public string LibelleVille { get; set; } = string.Empty;
    public Guid IdRegion { get; set; }
}
