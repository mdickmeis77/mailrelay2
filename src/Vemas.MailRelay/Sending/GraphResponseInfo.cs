namespace Vemas.MailRelay.Sending;

/// <summary>Kennung einer erfolgreichen Graph-Antwort für das Protokoll.</summary>
public sealed record GraphResponseInfo(int HttpStatus, string RequestId);
