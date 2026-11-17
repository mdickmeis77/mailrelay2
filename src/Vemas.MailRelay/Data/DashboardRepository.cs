using Vemas.MailRelay.Domain;

namespace Vemas.MailRelay.Data;

/// <summary>Kennzahlen für das Dashboard der Hoster-Oberfläche.</summary>
public sealed class DashboardRepository
{
    private const string DashboardSql = """
        DECLARE @today DATETIME2(3) = CAST(CAST(SYSUTCDATETIME() AS DATE) AS DATETIME2(3));
        SELECT
            (SELECT COUNT(*) FROM mailrelay.Message WHERE Status = 'Sent'   AND SentAt     >= @today),
            (SELECT COUNT(*) FROM mailrelay.Message WHERE Status = 'Failed' AND ReceivedAt >= @today),
            (SELECT COUNT(*) FROM mailrelay.Message WHERE Status IN ('Queued', 'Sending')),
            (SELECT COUNT(*) FROM mailrelay.Message WHERE Status = 'Retry'),
            (SELECT MAX(SentAt) FROM mailrelay.Message);
        """;

    private readonly SqlConnectionFactory _connectionFactory;

    public DashboardRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<DashboardStats> GetDashboardAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(DashboardSql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new DashboardStats
        {
            SentToday = reader.GetInt32(0),
            FailedToday = reader.GetInt32(1),
            InQueue = reader.GetInt32(2),
            InRetry = reader.GetInt32(3),
            LastSentAt = reader.IsDBNull(4) ? null : reader.GetDateTime(4).AsUtc()
        };
    }
}