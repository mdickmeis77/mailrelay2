using Vemas.MailRelay.Domain;

namespace Vemas.MailRelay.Data;

/// <summary>Queue-Operationen: Mail einstellen, reservieren und Ergebnis eines Versuchs speichern.</summary>
public sealed class MessageQueueRepository
{
    private const int LockMinutes = 15;

    private const string InsertSql = """
        INSERT INTO mailrelay.Message
            (SmtpUserId, ProfileId, CustomerId, ClientIp, EnvelopeFrom, EnvelopeTo, HeaderFrom, HeaderTo, Subject, SizeBytes, Mime, Status,
             HeaderCc, ReplyTo, BodyText, AttachmentNames)
        OUTPUT inserted.MessageId
        SELECT @SmtpUserId, @ProfileId, p.CustomerId, @ClientIp, @EnvelopeFrom, @EnvelopeTo, @HeaderFrom, @HeaderTo, @Subject, @SizeBytes, @Mime, 'Queued',
               @HeaderCc, @ReplyTo, @BodyText, @AttachmentNames
          FROM mailrelay.Profile p
         WHERE p.ProfileId = @ProfileId;
        """;

    // Fällige Mails reservieren. READPAST überspringt gesperrte Zeilen, abgelaufene
    // Sperren (z.B. nach Absturz während "Sending") werden wieder aufgenommen.
    private const string ClaimSql = """
        WITH due AS
        (
            SELECT TOP (@BatchSize) MessageId, ProfileId, EnvelopeTo, Mime, AttemptCount, Status, LockedUntil, Subject, ReceivedAt, DbMailItemId
              FROM mailrelay.Message WITH (ROWLOCK, UPDLOCK, READPAST)
             WHERE (Status IN ('Queued', 'Retry') AND NextAttemptAt <= SYSUTCDATETIME())
                OR (Status = 'Sending' AND LockedUntil < SYSUTCDATETIME())
             ORDER BY NextAttemptAt
        )
        UPDATE due
           SET Status = 'Sending',
               LockedUntil = DATEADD(MINUTE, @LockMinutes, SYSUTCDATETIME())
        OUTPUT inserted.MessageId, inserted.ProfileId, inserted.EnvelopeTo, inserted.Mime, inserted.AttemptCount,
               inserted.Subject, inserted.ReceivedAt, inserted.DbMailItemId;
        """;

    private const string CompleteSql = """
        UPDATE mailrelay.Message
           SET Status        = @Status,
               AttemptCount  = AttemptCount + 1,
               NextAttemptAt = ISNULL(@NextAttemptAt, NextAttemptAt),
               SentAt        = CASE WHEN @Status = 'Sent' THEN SYSUTCDATETIME() ELSE SentAt END,
               LastError     = @ErrorText,
               LockedUntil   = NULL
         WHERE MessageId = @MessageId;

        INSERT INTO mailrelay.Attempt (MessageId, AttemptType, Success, HttpStatus, ErrorText, GraphRequestId)
        VALUES (@MessageId, 'Send', @Success, @HttpStatus, @ErrorText, @RequestId);
        """;

    private const string DatabaseMailFeedbackSql = """
        UPDATE mailrelay.Message SET DbMailItemId = @MailItemId WHERE MessageId = @MessageId;

        INSERT INTO mailrelay.Attempt (MessageId, AttemptType, Success, ErrorText)
        VALUES (@MessageId, 'DbMail', 1, @Text);
        """;

    private readonly SqlConnectionFactory _connectionFactory;

    public MessageQueueRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<long> InsertAsync(NewMessage message, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(InsertSql);
        command.Add("@SmtpUserId", message.SmtpUserId);
        command.Add("@ProfileId", message.ProfileId);
        command.Add("@ClientIp", message.ClientIp);
        command.Add("@EnvelopeFrom", Truncate(message.EnvelopeFrom, 320));
        command.Add("@EnvelopeTo", string.Join(";", message.EnvelopeTo));
        command.Add("@HeaderFrom", Truncate(message.HeaderFrom, 320));
        command.Add("@HeaderTo", message.HeaderTo);
        command.Add("@Subject", Truncate(message.Subject, 1000));
        command.Add("@SizeBytes", message.Mime.Length);
        command.AddBinary("@Mime", message.Mime);
        command.Add("@HeaderCc", message.Content.HeaderCc);
        command.Add("@ReplyTo", Truncate(message.Content.ReplyTo, 320));
        command.Add("@BodyText", message.Content.BodyText);
        command.Add("@AttachmentNames", message.Content.AttachmentNames);

        object id = await command.ExecuteScalarAsync(cancellationToken);
        return id is null ? throw new InvalidOperationException($"Profil {message.ProfileId} existiert nicht.") : Convert.ToInt64(id);
    }

    public async Task<List<QueuedMessage>> ClaimBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        var result = new List<QueuedMessage>();
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(ClaimSql);
        command.Add("@BatchSize", batchSize);
        command.Add("@LockMinutes", LockMinutes);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new QueuedMessage
            {
                MessageId = reader.GetInt64(0),
                ProfileId = reader.GetInt32(1),
                EnvelopeTo = reader.GetString(2),
                Mime = reader.IsDBNull(3) ? null : (byte[])reader.GetValue(3),
                AttemptCount = reader.GetInt32(4),
                Subject = reader.IsDBNull(5) ? null : reader.GetString(5),
                ReceivedAtUtc = reader.GetDateTime(6).AsUtc(),
                DbMailItemId = reader.IsDBNull(7) ? null : reader.GetInt32(7)
            });
        }

        return result;
    }

    public Task MarkSentAsync(long messageId, int? httpStatus, string requestId, CancellationToken cancellationToken)
    {
        return CompleteAsync(messageId, MessageStatus.Sent, null, true, httpStatus, null, requestId, cancellationToken);
    }

    public Task MarkRetryAsync(long messageId, DateTime nextAttemptUtc, int? httpStatus, string error, string requestId, CancellationToken cancellationToken)
    {
        return CompleteAsync(messageId, MessageStatus.Retry, nextAttemptUtc, false, httpStatus, error, requestId, cancellationToken);
    }

    public Task MarkFailedAsync(long messageId, int? httpStatus, string error, string requestId, CancellationToken cancellationToken)
    {
        return CompleteAsync(messageId, MessageStatus.Failed, null, false, httpStatus, error, requestId, cancellationToken);
    }

    private async Task CompleteAsync(long messageId, string status, DateTime? nextAttemptUtc, bool success,
        int? httpStatus, string error, string requestId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(CompleteSql);
        command.Add("@MessageId", messageId);
        command.Add("@Status", status);
        command.Add("@NextAttemptAt", nextAttemptUtc);
        command.Add("@Success", success);
        command.Add("@HttpStatus", httpStatus);
        command.Add("@ErrorText", error);
        command.Add("@RequestId", Truncate(requestId, 100));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Hält eine Rückmeldung an Database Mail im Verlauf fest und merkt sich die zugeordnete Database-Mail.</summary>
    public async Task SaveDatabaseMailFeedbackAsync(long messageId, int mailItemId, string text, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(DatabaseMailFeedbackSql);
        command.Add("@MessageId", messageId);
        command.Add("@MailItemId", mailItemId);
        command.Add("@Text", text);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Truncate(string value, int maxLength)
    {
        return value is not null && value.Length > maxLength ? value[..maxLength] : value;
    }
}
