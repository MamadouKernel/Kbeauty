using KekeBeauty.SharedKernel;
using NetTopologySuite.Geometries;

namespace KekeBeauty.Modules.Geography.Domain;

/// <summary>Country > Region > City > Commune hierarchy (PRD §7.2, SRCH-02).</summary>
public sealed class Country : Entity
{
    public required string Iso2 { get; init; }
    public required string Name { get; set; }
    public required string PhonePrefix { get; init; }
    public required string CurrencyCode { get; init; }
    public bool IsActive { get; set; } = true;
    public List<Region> Regions { get; } = [];
}

public sealed class Region : Entity
{
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public List<City> Cities { get; } = [];
}

public sealed class City : Entity
{
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public List<Commune> Communes { get; } = [];
}

public sealed class Commune : Entity
{
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public Point? Centroid { get; init; }
}
