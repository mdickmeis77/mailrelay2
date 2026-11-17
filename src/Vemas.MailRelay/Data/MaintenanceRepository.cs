using System.Data;
using Vemas.MailRelay.Domain;

namespace Vemas.MailRelay.Data;

/// <summary>Datenzugriff für Hintergrundaufgaben: Klartextfelder übernehmen, Aufräumen.</summary>
public sealed class MaintenanceRepository
{
    private const int CleanupTimeoutSeconds = 600;

    private readonly SqlConnectionFactory _connectionFactory;

    public MaintenanceRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<List<PendingPlaintext>> GetPendingSecretsAsync(CancellationToken cancellationToken)
    {
        return GetPendingAsync(
            "SELECT ProfileId, SecretPlain FROM mailrelay.Profile WHERE SecretPlain IS NOT NULL AND SecretPlain <> N''",
            cancellationToken);
    }

    public Task<List<PendingPlaintext>> GetPendingPasswordsAsync(CancellationToken cancellationToken)
    {
        return GetPendingAsync(
            "SELECT SmtpUserId, PasswordPlain FROM mailrelay.SmtpUser WHERE PasswordPlain IS NOT NULL AND PasswordPlain <> N''",
            cancellationToken);
    }

    /// <summary>Speichert das verschlüsselte Secret und leert das Klartextfeld.</summary>
    public async Task StoreSecretAsync(int profileId, byte[] secretEncrypted, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand("""
            UPDATE mailrelay.Profile
               SET SecretEncrypted = @Secret, SecretPlain = NULL, ModifiedAt = SYSUTCDATETIME()
             WHERE ProfileId = @Id
            """);
        command.Add("@Id", profileId);
        command.AddBinary("@Secret", secretEncrypted);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Speichert den Passwort-Hash und leert das Klartextfeld.</summary>
    public async Task StorePasswordHashAsync(int smtpUserId, string passwordHash, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand("""
            UPDATE mailrelay.SmtpUser
               SET PasswordHash = @Hash, PasswordPlain = NULL, ModifiedAt = SYSUTCDATETIME()
             WHERE SmtpUserId = @Id
            """);
        command.Add("@Id", smtpUserId);
        command.Add("@Hash", passwordHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CleanupAsync(int mimeRetentionDays, int logRetentionDays, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand("mailrelay.Cleanup");
        command.CommandType = CommandType.StoredProcedure;
        command.CommandTimeout = CleanupTimeoutSeconds;
        command.Add("@MimeRetentionDays", mimeRetentionDays);
        command.Add("@LogRetentionDays", logRetentionDays);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<List<PendingPlaintext>> GetPendingAsync(string sql, CancellationToken cancellationToken)
    {
        var result = new List<PendingPlaintext>();
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new PendingPlaintext { Id = reader.GetInt32(0), Plaintext = reader.GetString(1) });
        }

        return result;
    }
}
