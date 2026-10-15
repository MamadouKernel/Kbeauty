namespace KekeBeauty.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL functions usable inside LINQ queries. The bodies never run: EF Core translates calls to SQL
/// (registered in <see cref="KekeBeautyDbContext"/>).
/// </summary>
public static class PostgresFunctions
{
    /// <summary>
    /// SQL <c>public.keke_unaccent(text)</c>: <c>unaccent</c> declared IMMUTABLE so that a trigram index can be built on it
    /// (the stock function is only STABLE, which forces a full scan on every accent-insensitive search).
    /// </summary>
    public static string Unaccent(string value) =>
        throw new NotSupportedException("This method is translated to the SQL unaccent() function.");
}
