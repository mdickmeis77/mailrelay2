using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Vemas.MailRelay.Options;

namespace Vemas.MailRelay.Data;

/// <summary>Ruft msdb.dbo.mailrelay_StatusMelden auf (Rückmeldung an SQL Server Database Mail).</summary>
public sealed class DatabaseMailRepository
{
    private const string ProcedureName = "dbo.mailrelay_StatusMelden";
    private const int CommandTimeoutSeconds = 15;

    // Erst beim ersten Aufruf auswerten: eine fehlerhafte Einstellung darf den Dienststart nie verhindern.
    private readonly Lazy<string> _connectionString;

    public DatabaseMailRepository(IConfiguration configuration, IOptions<DatabaseMailOptions> options)
    {
        string configured = options.Value.ConnectionString;
        string relay = configuration.GetConnectionString(SqlConnectionFactory.ConnectionStringName);
        _connectionString = new Lazy<string>(() => BuildConnectionString(configured, relay));
    }

    private static string BuildConnectionString(string configured, string relay)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        // Standard: dieselbe Instanz und Anmeldung wie die Relay-Datenbank, Datenbank msdb
        return new SqlConnectionStringBuilder(relay) { InitialCatalog = "msdb" }.ConnectionString;
    }

    /// <summary>Meldet einen endgültigen Fehler. Liefert die Nr. der zugeordneten Database-Mail oder 0.</summary>
    public Task<int> ReportFailedAsync(long relayMessageId, string subject, string recipients, DateTime receivedUtc,
        string errorText, CancellationToken cancellationToken)
    {
        return ExecuteAsync(relayMessageId, true, subject, recipients, receivedUtc, errorText, null, cancellationToken);
    }

    /// <summary>Meldet die Zustellung nach erneutem Senden für eine bereits zugeordnete Database-Mail.</summary>
    public Task<int> ReportSentAsync(long relayMessageId, int mailItemId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(relayMessageId, false, null, null, null, null, mailItemId, cancellationToken);
    }

    private async Task<int> ExecuteAsync(long relayMessageId, bool failed, string subject, string recipients,
        DateTime? receivedUtc, string errorText, int? mailItemId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString.Value);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(ProcedureName);
        command.CommandType = CommandType.StoredProcedure;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.Add("@RelayMessageId", relayMessageId);
        command.Add("@Failed", failed);
        command.Add("@Subject", subject);
        command.Add("@Recipients", recipients);
        command.Add("@ReceivedUtc", receivedUtc);
        command.Add("@ErrorText", errorText is { Length: > 3000 } ? errorText[..3000] : errorText);
        var output = command.Parameters.Add("@MailItemId", SqlDbType.Int);
        output.Direction = ParameterDirection.InputOutput;
        output.Value = (object)mailItemId ?? DBNull.Value;

        await command.ExecuteNonQueryAsync(cancellationToken);
        return output.Value is int id ? id : 0;
    }
}
