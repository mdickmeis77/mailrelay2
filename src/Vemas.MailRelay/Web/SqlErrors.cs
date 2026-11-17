using Microsoft.Data.SqlClient;

namespace Vemas.MailRelay.Web;

/// <summary>Erkennung typischer SQL-Fehler für verständliche Meldungen in der Oberfläche.</summary>
public static class SqlErrors
{
    private const int UniqueConstraintViolation = 2627;
    private const int UniqueIndexViolation = 2601;

    public static bool IsDuplicateKey(SqlException exception)
    {
        return exception.Number is UniqueConstraintViolation or UniqueIndexViolation;
    }
}
