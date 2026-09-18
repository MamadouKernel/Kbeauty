namespace KekeBeauty.Domain.Entities;

public class Region
{
    public Guid IdRegion { get; set; }
    public string LibelleRegion { get; set; } = string.Empty;
    public Guid IdPays { get; set; }
}
