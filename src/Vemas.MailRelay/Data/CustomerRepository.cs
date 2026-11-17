using Microsoft.Data.SqlClient;
using Vemas.MailRelay.Domain;

namespace Vemas.MailRelay.Data;

/// <summary>Lesen und Schreiben von Kunden und ihrer Login-Zuordnung.</summary>
public sealed class CustomerRepository
{
    private const string SaveSql = """
        IF @CustomerId = 0
        BEGIN
            INSERT INTO mailrelay.Customer (Name, ShortName, IsActive) VALUES (@Name, @ShortName, @IsActive);
            SET @CustomerId = CAST(SCOPE_IDENTITY() AS INT);
        END
        ELSE
        BEGIN
            UPDATE mailrelay.Customer SET Name = @Name, ShortName = @ShortName, IsActive = @IsActive
             WHERE CustomerId = @CustomerId;
        END

        DELETE FROM mailrelay.CustomerLogin WHERE CustomerId = @CustomerId;

        INSERT INTO mailrelay.CustomerLogin (CustomerId, LoginName)
        SELECT DISTINCT @CustomerId, LTRIM(RTRIM(value))
          FROM STRING_SPLIT(@Logins, NCHAR(10))
         WHERE LTRIM(RTRIM(value)) <> N'';

        SELECT @CustomerId;
        """;

    private readonly SqlConnectionFactory _connectionFactory;

    public CustomerRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<List<Customer>> GetAllAsync(CancellationToken cancellationToken)
    {
        var customers = new Dictionary<int, Customer>();
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);

        await using (var command = connection.CreateCommand("SELECT CustomerId, Name, ShortName, IsActive FROM mailrelay.Customer ORDER BY Name"))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var customer = new Customer
                {
                    CustomerId = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    ShortName = reader.GetString(2),
                    IsActive = reader.GetBoolean(3)
                };
                customers.Add(customer.CustomerId, customer);
            }
        }

        await using (var command = connection.CreateCommand("SELECT CustomerId, LoginName FROM mailrelay.CustomerLogin ORDER BY LoginName"))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (customers.TryGetValue(reader.GetInt32(0), out var customer))
                {
                    customer.Logins.Add(reader.GetString(1));
                }
            }
        }

        return [.. customers.Values];
    }

    public async Task<Customer> GetAsync(int customerId, CancellationToken cancellationToken)
    {
        var all = await GetAllAsync(cancellationToken);
        return all.FirstOrDefault(c => c.CustomerId == customerId);
    }

    public async Task<int> SaveAsync(Customer customer, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand(SaveSql);
        command.Transaction = transaction;
        command.Add("@CustomerId", customer.CustomerId);
        command.Add("@Name", customer.Name.Trim());
        command.Add("@ShortName", customer.ShortName.Trim());
        command.Add("@IsActive", customer.IsActive);
        command.Add("@Logins", string.Join('\n', customer.Logins));

        int id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return id;
    }
}
