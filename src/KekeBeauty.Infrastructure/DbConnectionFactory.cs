using Microsoft.Extensions.Configuration;
using Npgsql;

namespace KekeBeauty.Infrastructure;

public interface IDbConnectionFactory
{
    NpgsqlConnection CreateConnection();
}

public class DbConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public DbConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "La chaine de connexion 'Default' est absente de la configuration (ConnectionStrings__Default).");
    }

    public NpgsqlConnection CreateConnection() => new(_connectionString);
}
