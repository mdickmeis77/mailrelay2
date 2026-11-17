using System.Net;

namespace Vemas.MailRelay.Sending;

/// <summary>Fehlerantwort der Microsoft Graph API.</summary>
public sealed class GraphRequestException : Exception
{
    private static readonly HashSet<HttpStatusCode> TransientStatusCodes =
    [
        HttpStatusCode.RequestTimeout,
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout
    ];

    public GraphRequestException(HttpStatusCode statusCode, string message, string requestId)
        : base(message)
    {
        StatusCode = statusCode;
        RequestId = requestId;
    }

    public HttpStatusCode StatusCode { get; }

    public string RequestId { get; }

    public bool IsTransient => TransientStatusCodes.Contains(StatusCode);
}
