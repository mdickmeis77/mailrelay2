using Microsoft.Data.SqlClient;
using Vemas.MailRelay.Domain;

namespace Vemas.MailRelay.Data;

/// <summary>Lesen und Schreiben von Versandprofilen und erlaubten Absendern.</summary>
public sealed class ProfileRepository
{
    private const string SelectProfiles = """
        SELECT p.ProfileId, p.Name, p.TenantId, p.ClientId, p.SecretEncrypted, p.SecretExpiresAt,
               p.SenderMode, p.DefaultSender, p.IsActive, p.ModifiedAt
          FROM mailrelay.Profile p
         ORDER BY p.Name
        """;

    private const string SaveSql = """
        IF @ProfileId = 0
        BEGIN
            INSERT INTO mailrelay.Profile (Name, TenantId, ClientId, SecretEncrypted, SecretExpiresAt, SenderMode, DefaultSender, IsActive)
            VALUES (@Name, @TenantId, @ClientId, @Secret, @SecretExpiresAt, @SenderMode, @DefaultSender, @IsActive);
            SET @ProfileId = CAST(SCOPE_IDENTITY() AS INT);
        END
        ELSE
        BEGIN
            UPDATE mailrelay.Profile
               SET Name = @Name, TenantId = @TenantId, ClientId = @ClientId,
                   SecretEncrypted = ISNULL(@Secret, SecretEncrypted),
                   SecretExpiresAt = @SecretExpiresAt, SenderMode = @SenderMode,
                   DefaultSender = @DefaultSender, IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME()
             WHERE ProfileId = @ProfileId;
        END

        DELETE FROM mailrelay.AllowedSender WHERE ProfileId = @ProfileId;

        INSERT INTO mailrelay.AllowedSender (ProfileId, Pattern)
        SELECT DISTINCT @ProfileId, LTRIM(RTRIM(value))
          FROM STRING_SPLIT(@Senders, NCHAR(10))
         WHERE LTRIM(RTRIM(value)) <> N'';

        SELECT @ProfileId;
        """;

    private readonly SqlConnectionFactory _connectionFactory;

    public ProfileRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<List<RelayProfile>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var profiles = new Dictionary<int, RelayProfile>();

        await using (var command = connection.CreateCommand(SelectProfiles))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var profile = ReadProfile(reader);
                profiles.Add(profile.ProfileId, profile);
            }
        }

        await using (var command = connection.CreateCommand("SELECT ProfileId, Pattern FROM mailrelay.AllowedSender ORDER BY Pattern"))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (profiles.TryGetValue(reader.GetInt32(0), out var profile))
                {
                    profile.AllowedSenders.Add(reader.GetString(1));
                }
            }
        }

        return [.. profiles.Values];
    }

    public async Task<RelayProfile> GetAsync(int profileId, CancellationToken cancellationToken)
    {
        var all = await GetAllAsync(cancellationToken);
        return all.FirstOrDefault(p => p.ProfileId == profileId);
    }

    /// <summary>Legt ein Profil an oder aktualisiert es. Das Secret wird nur geschrieben, wenn übergeben.</summary>
    public async Task<int> SaveAsync(RelayProfile profile, byte[] newSecretEncrypted, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand(SaveSql);
        command.Transaction = transaction;
        command.Add("@ProfileId", profile.ProfileId);
        command.Add("@Name", profile.Name.Trim());
        command.Add("@TenantId", profile.TenantId.Trim());
        command.Add("@ClientId", profile.ClientId.Trim());
        command.AddBinary("@Secret", newSecretEncrypted);
        command.Add("@SecretExpiresAt", profile.SecretExpiresAt?.Date);
        command.Add("@SenderMode", profile.SenderMode.ToString());
        command.Add("@DefaultSender", profile.DefaultSender.Trim());
        command.Add("@IsActive", profile.IsActive);
        command.Add("@Senders", string.Join('\n', profile.AllowedSenders));

        int id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    private static RelayProfile ReadProfile(SqlDataReader reader)
    {
        return new RelayProfile
        {
            ProfileId = reader.GetInt32(reader.GetOrdinal("ProfileId")),
            Name = reader.GetString(reader.GetOrdinal("Name")),
            TenantId = reader.GetString(reader.GetOrdinal("TenantId")),
            ClientId = reader.GetString(reader.GetOrdinal("ClientId")),
            SecretEncrypted = reader.GetBytesOrNull("SecretEncrypted"),
            SecretExpiresAt = reader.GetDateTimeOrNull("SecretExpiresAt"),
            SenderMode = Enum.Parse<SenderMode>(reader.GetString(reader.GetOrdinal("SenderMode"))),
            DefaultSender = reader.GetString(reader.GetOrdinal("DefaultSender")),
            IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
            ModifiedAt = reader.GetDateTime(reader.GetOrdinal("ModifiedAt")).AsUtc()
        };
    }
}
