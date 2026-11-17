namespace Vemas.MailRelay.Sending;

/// <summary>Anhang, der für den Upload aus dem MIME herausgelöst wurde.</summary>
public sealed record AttachmentData(string Name, string ContentType, byte[] Content);
