namespace Vemas.MailRelay.Sending;

/// <summary>
/// Eingeordneter Versandfehler. IsTransient = true bedeutet: erneuter Versuch sinnvoll
/// (Netzwerk, Drosselung, Serverfehler).
/// </summary>
public sealed record SendError(bool IsTransient, int? HttpStatus, string Message, string RequestId);
