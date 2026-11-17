using Microsoft.Data.SqlClient;
using System.Data;
using Vemas.MailRelay.Domain;

namespace Vemas.MailRelay.Data;

/// <summary>Protokoll für die Weboberfläche: Suche, Details, MIME-Download, Neu senden, Abbrechen.</summary>
public sealed class MessageLogRepository
{
    private const string ListColumns = """
        m.MessageId, m.ReceivedAt, u.Login AS SmtpUser, p.Name AS ProfileName, c.Name AS CustomerName, m.HeaderFrom, m.EnvelopeTo,
        m.Subject, m.SizeBytes, m.Status, m.AttemptCount, m.SentAt, m.LastError,
        CAST(CASE WHEN m.Mime IS NULL THEN 0 ELSE 1 END AS BIT) AS HasMime
        """;

    private const string FromJoins = """
        FROM mailrelay.Message m
        LEFT JOIN mailrelay.SmtpUser u ON u.SmtpUserId = m.SmtpUserId
        LEFT JOIN mailrelay.Profile  p ON p.ProfileId  = m.ProfileId
        LEFT JOIN mailrelay.Customer c ON c.CustomerId = m.CustomerId
        """;

    private const string SearchWhere = """
        WHERE (@Status IS NULL OR m.Status = @Status)
          AND (@CustomerId IS NULL OR m.CustomerId = @CustomerId)
          AND (@FromUtc IS NULL OR m.ReceivedAt >= @FromUtc)
          AND (@ToUtc   IS NULL OR m.ReceivedAt <  @ToUtc)
          AND (@Search  IS NULL OR m.HeaderFrom LIKE @Search OR m.EnvelopeTo LIKE @Search OR m.Subject LIKE @Search)
        """;

    private readonly SqlConnectionFactory _connectionFactory;

    public MessageLogRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<(List<MessageListItem> Items, int Total)> SearchAsync(MessageFilter filter, CancellationToken cancellationToken)
    {
        string sql = $"""
            SELECT COUNT(*) {FromJoins} {SearchWhere};
            SELECT {ListColumns} {FromJoins} {SearchWhere}
             ORDER BY m.MessageId DESC
             OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(sql);
        command.Add("@Status", string.IsNullOrEmpty(filter.Status) ? null : filter.Status);
        command.Add("@CustomerId", filter.CustomerId);
        command.Add("@FromUtc", filter.FromDate?.Date.ToUniversalTime());
        command.Add("@ToUtc", filter.ToDate?.Date.AddDays(1).ToUniversalTime());
        command.Add("@Search", string.IsNullOrWhiteSpace(filter.Search) ? null : $"%{filter.Search.Trim()}%");
        command.Add("@Skip", (Math.Max(filter.Page, 1) - 1) * filter.PageSize);
        command.Add("@Take", filter.PageSize);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        int total = reader.GetInt32(0);

        var items = new List<MessageListItem>();
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadListItem(reader));
        }

        return (items, total);
    }

    public async Task<MessageDetails> GetDetailsAsync(long messageId, CancellationToken cancellationToken)
    {
        string sql = $"""
            SELECT {ListColumns}, m.ClientIp, m.EnvelopeFrom, m.HeaderTo, m.NextAttemptAt, m.MimePurgedAt
              {FromJoins}
             WHERE m.MessageId = @Id;

            SELECT AttemptAt, AttemptType, Success, HttpStatus, ErrorText, GraphRequestId, UserName
              FROM mailrelay.Attempt
             WHERE MessageId = @Id
             ORDER BY AttemptId;
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(sql);
        command.Add("@Id", messageId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var details = new MessageDetails
        {
            Summary = ReadListItem(reader),
            ClientIp = reader.GetStringOrNull("ClientIp"),
            EnvelopeFrom = reader.GetStringOrNull("EnvelopeFrom"),
            HeaderTo = reader.GetStringOrNull("HeaderTo"),
            NextAttemptAt = reader.GetDateTime(reader.GetOrdinal("NextAttemptAt")).AsUtc(),
            MimePurgedAt = reader.GetDateTimeOrNull("MimePurgedAt")?.AsUtc()
        };

        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            details.Attempts.Add(new AttemptItem
            {
                AttemptAt = reader.GetDateTime(0).AsUtc(),
                AttemptType = reader.GetString(1),
                Success = reader.GetBooleanOrNull("Success"),
                HttpStatus = reader.GetInt32OrNull("HttpStatus"),
                ErrorText = reader.GetStringOrNull("ErrorText"),
                GraphRequestId = reader.GetStringOrNull("GraphRequestId"),
                UserName = reader.GetStringOrNull("UserName")
            });
        }

        return details;
    }

    public async Task<byte[]> GetMimeAsync(long messageId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand("SELECT Mime FROM mailrelay.Message WHERE MessageId = @Id");
        command.Add("@Id", messageId);
        object value = await command.ExecuteScalarAsync(cancellationToken);
        return value as byte[];
    }

    /// <summary>Ruft mailrelay.Resend auf. Liefert false, wenn die Mail nicht neu gesendet werden kann.</summary>
    public Task<bool> ResendAsync(long messageId, string userName, CancellationToken cancellationToken)
    {
        return ExecuteActionAsync("mailrelay.Resend", messageId, userName, cancellationToken);
    }

    /// <summary>Ruft mailrelay.Cancel auf. Liefert false, wenn die Mail nicht (mehr) abgebrochen werden kann.</summary>
    public Task<bool> CancelAsync(long messageId, string userName, CancellationToken cancellationToken)
    {
        return ExecuteActionAsync("mailrelay.Cancel", messageId, userName, cancellationToken);
    }

    private async Task<bool> ExecuteActionAsync(string procedure, long messageId, string userName, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(procedure);
        command.CommandType = CommandType.StoredProcedure;
        command.Add("@MessageId", messageId);
        command.Add("@UserName", userName);
        var returnValue = command.Parameters.Add("@ReturnValue", SqlDbType.Int);
        returnValue.Direction = ParameterDirection.ReturnValue;
        await command.ExecuteNonQueryAsync(cancellationToken);
        return (int)returnValue.Value == 1;
    }

    private static MessageListItem ReadListItem(SqlDataReader reader)
    {
        return new MessageListItem
        {
            MessageId = reader.GetInt64(reader.GetOrdinal("MessageId")),
            ReceivedAt = reader.GetDateTime(reader.GetOrdinal("ReceivedAt")).AsUtc(),
            SmtpUser = reader.GetStringOrNull("SmtpUser"),
            ProfileName = reader.GetStringOrNull("ProfileName"),
            CustomerName = reader.GetStringOrNull("CustomerName"),
            HeaderFrom = reader.GetStringOrNull("HeaderFrom"),
            EnvelopeTo = reader.GetString(reader.GetOrdinal("EnvelopeTo")),
            Subject = reader.GetStringOrNull("Subject"),
            SizeBytes = reader.GetInt32(reader.GetOrdinal("SizeBytes")),
            Status = reader.GetString(reader.GetOrdinal("Status")),
            AttemptCount = reader.GetInt32(reader.GetOrdinal("AttemptCount")),
            SentAt = reader.GetDateTimeOrNull("SentAt")?.AsUtc(),
            LastError = reader.GetStringOrNull("LastError"),
            HasMime = reader.GetBoolean(reader.GetOrdinal("HasMime"))
        };
    }
}
