using SmtpServer;
using SmtpServer.Net;
using System.Net;

namespace Vemas.MailRelay.Smtp;

/// <summary>Zugriff auf sitzungsbezogene Werte der SMTP-Verbindung.</summary>
internal static class SessionContextExtensions
{
    private const string SmtpUserIdKey = "Vemas.MailRelay.SmtpUserId";

    public static IPAddress GetRemoteAddress(this ISessionContext context)
    {
        if (context.Properties.TryGetValue(EndpointListener.RemoteEndPointKey, out object value) && value is IPEndPoint endPoint)
        {
            return endPoint.Address.IsIPv4MappedToIPv6 ? endPoint.Address.MapToIPv4() : endPoint.Address;
        }

        return null;
    }

    public static void SetSmtpUserId(this ISessionContext context, int smtpUserId)
    {
        context.Properties[SmtpUserIdKey] = smtpUserId;
    }

    public static int? GetSmtpUserId(this ISessionContext context)
    {
        return context.Properties.TryGetValue(SmtpUserIdKey, out object value) && value is int id ? id : null;
    }
}
