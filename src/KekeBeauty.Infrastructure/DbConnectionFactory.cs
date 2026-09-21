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
        var connectionString = configuration.GetConnectionString("Default");
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new InvalidOperationException(
                "La chaine de connexion 'Default' est absente de la configuration (ConnectionStrings__Default). " +
                "En local (dotnet run, hors Docker), renseignez ConnectionStrings:Default dans appsettings.Development.json.");
    }

    public NpgsqlConnection CreateConnection() => new(_connectionString);
}
