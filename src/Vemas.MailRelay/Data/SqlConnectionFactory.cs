using Microsoft.Data.SqlClient;

namespace Vemas.MailRelay.Data;

/// <summary>Erzeugt geöffnete Verbindungen zur NextGen-Datenbank (Schema mailrelay).</summary>
public sealed class SqlConnectionFactory
{
    public const string ConnectionStringName = "MailRelay";

    private readonly string _connectionString;

    public SqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException($"ConnectionStrings:{ConnectionStringName} fehlt in der appsettings.json.");
        }
    }

    public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
