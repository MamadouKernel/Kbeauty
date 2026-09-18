namespace KekeBeauty.Web.Models;

public sealed class EtablissementGere
{
    public Guid IdEtablissement { get; set; }
    public string NomEtablissement { get; set; } = string.Empty;
    public string StatutKyc { get; set; } = string.Empty;
    public bool EstSuspendu { get; set; }
}

public sealed class RdvPartenaire
{
    public Guid IdRdv { get; set; }
    public string StatutRdv { get; set; } = string.Empty;
    public DateTimeOffset DateHeureDebut { get; set; }
    public string LibellePrestation { get; set; } = string.Empty;
    public string TelephoneClient { get; set; } = string.Empty;
}
