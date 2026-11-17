using Microsoft.Data.SqlClient;
using Vemas.MailRelay.Domain;

namespace Vemas.MailRelay.Data;

/// <summary>Lesen und Schreiben von SMTP-Benutzern.</summary>
public sealed class SmtpUserRepository
{
    private const string SelectUsers = """
        SELECT SmtpUserId, Login, PasswordHash, ProfileId, AllowedIps, IsActive, LastLoginAt
          FROM mailrelay.SmtpUser
         ORDER BY Login
        """;

    private const string SaveSql = """
        IF @SmtpUserId = 0
        BEGIN
            INSERT INTO mailrelay.SmtpUser (Login, PasswordHash, ProfileId, AllowedIps, IsActive)
            VALUES (@Login, @PasswordHash, @ProfileId, @AllowedIps, @IsActive);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
        END
        ELSE
        BEGIN
            UPDATE mailrelay.SmtpUser
               SET Login = @Login, PasswordHash = ISNULL(@PasswordHash, PasswordHash),
                   ProfileId = @ProfileId, AllowedIps = @AllowedIps, IsActive = @IsActive,
                   ModifiedAt = SYSUTCDATETIME()
             WHERE SmtpUserId = @SmtpUserId;
            SELECT @SmtpUserId;
        END
        """;

    private readonly SqlConnectionFactory _connectionFactory;

    public SmtpUserRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<List<RelaySmtpUser>> GetAllAsync(CancellationToken cancellationToken)
    {
        var users = new List<RelaySmtpUser>();
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(SelectUsers);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(ReadUser(reader));
        }

        return users;
    }

    public async Task<RelaySmtpUser> GetAsync(int smtpUserId, CancellationToken cancellationToken)
    {
        var all = await GetAllAsync(cancellationToken);
        return all.FirstOrDefault(u => u.SmtpUserId == smtpUserId);
    }

    /// <summary>Legt einen Benutzer an oder aktualisiert ihn. Der Hash wird nur geschrieben, wenn übergeben.</summary>
    public async Task<int> SaveAsync(RelaySmtpUser user, string newPasswordHash, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(SaveSql);
        command.Add("@SmtpUserId", user.SmtpUserId);
        command.Add("@Login", user.Login.Trim());
        command.Add("@PasswordHash", newPasswordHash);
        command.Add("@ProfileId", user.ProfileId);
        command.Add("@AllowedIps", string.IsNullOrWhiteSpace(user.AllowedIps) ? null : user.AllowedIps.Trim());
        command.Add("@IsActive", user.IsActive);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task UpdateLastLoginAsync(int smtpUserId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand(
            "UPDATE mailrelay.SmtpUser SET LastLoginAt = SYSUTCDATETIME() WHERE SmtpUserId = @Id");
        command.Add("@Id", smtpUserId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static RelaySmtpUser ReadUser(SqlDataReader reader)
    {
        return new RelaySmtpUser
        {
            SmtpUserId = reader.GetInt32(reader.GetOrdinal("SmtpUserId")),
            Login = reader.GetString(reader.GetOrdinal("Login")),
            PasswordHash = reader.GetStringOrNull("PasswordHash"),
            ProfileId = reader.GetInt32(reader.GetOrdinal("ProfileId")),
            AllowedIps = reader.GetStringOrNull("AllowedIps"),
            IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
            LastLoginAt = reader.GetDateTimeOrNull("LastLoginAt")?.AsUtc()
        };
    }
}
