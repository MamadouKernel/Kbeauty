namespace KekeBeauty.Domain.Entities;

public class Prestation
{
    public Guid IdPrestation { get; set; }
    public string LibellePrestation { get; set; } = string.Empty;
    public decimal Tarif { get; set; }
    public short DureeMinutes { get; set; }
    public Guid IdEtablissement { get; set; }
}
