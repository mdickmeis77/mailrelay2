using Vemas.MailRelay.Domain;

namespace Vemas.MailRelay.Data;

/// <summary>Testmail-Aufträge aus NextGen reservieren und das Ergebnis eintragen.</summary>
public sealed class TestMailRequestRepository
{
    private const int BatchSize = 20;

    // Reservieren durch Setzen von ProcessedAt; READPAST überspringt gesperrte Zeilen.
    private const string ClaimSql = """
        WITH pending AS
        (
            SELECT TOP (@BatchSize) TestMailRequestId, ProfileId, Recipient, RequestedBy, ProcessedAt
              FROM mailrelay.TestMailRequest WITH (ROWLOCK, UPDLOCK, READPAST)
             WHERE ProcessedAt IS NULL
             ORDER BY TestMailRequestId
        )
        UPDATE pending SET ProcessedAt = SYSUTCDATETIME()
        OUTPUT inserted.TestMailRequestId, inserted.ProfileId, inserted.Recipient, inserted.RequestedBy;
        """;

    private readonly SqlConnectionFactory _connectionFactory;

    public TestMailRequestRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<List<TestMailRequest>> ClaimPendingAsync(CancellationToken cancellationToken)
    {
        var result = new List<TestMailRequest>();
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(ClaimSql);
        command.Add("@BatchSize", BatchSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new TestMailRequest
            {
                TestMailRequestId = reader.GetInt32(0),
                ProfileId = reader.GetInt32(1),
                Recipient = reader.GetString(2),
                RequestedBy = reader.IsDBNull(3) ? null : reader.GetString(3)
            });
        }

        return result;
    }

    public async Task CompleteAsync(int requestId, long? messageId, string error, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(
            "UPDATE mailrelay.TestMailRequest SET MessageId = @MessageId, ErrorText = @Error WHERE TestMailRequestId = @Id");
        command.Add("@Id", requestId);
        command.Add("@MessageId", messageId);
        command.Add("@Error", error);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
