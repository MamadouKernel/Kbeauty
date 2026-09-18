namespace KekeBeauty.Domain.Entities;

public enum TypeMedia
{
    Photo,
    Video,
}

public class Media
{
    public Guid IdMedia { get; set; }
    public TypeMedia TypeMedia { get; set; }
    public string Url { get; set; } = string.Empty;
    public short OrdreAffichage { get; set; }
    public Guid IdEtablissement { get; set; }
}
