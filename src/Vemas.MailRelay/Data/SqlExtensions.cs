using Microsoft.Data.SqlClient;
using System.Data;

namespace Vemas.MailRelay.Data;

/// <summary>Kleine Helfer für ADO.NET (Parameter mit NULL-Behandlung, Nullable-Spalten).</summary>
internal static class SqlExtensions
{
    public static SqlCommand CreateCommand(this SqlConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    public static SqlParameter Add(this SqlCommand command, string name, object value)
    {
        return command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    public static SqlParameter AddBinary(this SqlCommand command, string name, byte[] value)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.VarBinary, -1);
        parameter.Value = (object)value ?? DBNull.Value;
        return parameter;
    }

    public static string GetStringOrNull(this SqlDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    public static DateTime? GetDateTimeOrNull(this SqlDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }

    public static int? GetInt32OrNull(this SqlDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    public static bool? GetBooleanOrNull(this SqlDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetBoolean(ordinal);
    }

    public static byte[] GetBytesOrNull(this SqlDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : (byte[])reader.GetValue(ordinal);
    }

    /// <summary>Datenbankwerte sind UTC; Kind setzen, damit ToLocalTime() korrekt arbeitet.</summary>
    public static DateTime AsUtc(this DateTime value)
    {
        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }
}
