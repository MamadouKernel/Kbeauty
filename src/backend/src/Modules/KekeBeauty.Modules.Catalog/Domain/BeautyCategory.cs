using KekeBeauty.SharedKernel;

namespace KekeBeauty.Modules.Catalog.Domain;

/// <summary>Beauty category (hair salon, spa, ...) — administrable list (PRD SRCH-01, ADM-05).</summary>
public sealed class BeautyCategory : Entity
{
    public required string Name { get; set; }
    public required string Slug { get; init; }
    public required string Icon { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
