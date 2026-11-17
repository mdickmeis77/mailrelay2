using MimeKit;

namespace Vemas.MailRelay.Sending;

/// <summary>
/// Versand von Mails über 4 MB: Entwurf ohne Anhänge anlegen, Anhänge einzeln bzw. per
/// Upload-Session hochladen, Entwurf senden. Bei Fehlern wird der Entwurf wieder gelöscht.
/// </summary>
public sealed class LargeMessageSender
{
    /// <summary>Anhänge bis zu dieser Größe werden direkt angehängt, größere per Upload-Session.</summary>
    private const int DirectAttachmentLimitBytes = 3 * 1024 * 1024;

    /// <summary>Blockgröße für Upload-Sessions, muss ein Vielfaches von 320 KiB sein.</summary>
    private const int UploadChunkBytes = 10 * 320 * 1024;

    private const string DefaultAttachmentName = "Anhang";

    private readonly GraphMailClient _graphClient;
    private readonly ILogger<LargeMessageSender> _logger;

    public LargeMessageSender(GraphMailClient graphClient, ILogger<LargeMessageSender> logger)
    {
        _graphClient = graphClient;
        _logger = logger;
    }

    public async Task<GraphResponseInfo> SendAsync(string token, string sender, MimeMessage message, CancellationToken cancellationToken)
    {
        var attachments = await DetachAttachmentsAsync(message, cancellationToken);
        byte[] bodyMime = await MimeSerializer.SerializeAsync(message, cancellationToken);
        if (!MimeSerializer.FitsIntoSingleRequest(bodyMime.Length))
        {
            throw new PermanentSendException(
                $"Mailtext ohne Anhänge ist mit {bodyMime.Length / 1024} KB zu groß für Microsoft Graph (max. ca. 2,9 MB).");
        }

        string draftId = await _graphClient.CreateDraftFromMimeAsync(token, sender, bodyMime, cancellationToken);
        try
        {
            foreach (var attachment in attachments)
            {
                await UploadAttachmentAsync(token, sender, draftId, attachment, cancellationToken);
            }

            return await _graphClient.SendDraftAsync(token, sender, draftId, cancellationToken);
        }
        catch (Exception)
        {
            await TryDeleteDraftAsync(token, sender, draftId);
            throw;
        }
    }

    private async Task UploadAttachmentAsync(string token, string sender, string draftId, AttachmentData attachment, CancellationToken cancellationToken)
    {
        if (attachment.Content.Length <= DirectAttachmentLimitBytes)
        {
            await _graphClient.AddAttachmentAsync(token, sender, draftId, attachment, cancellationToken);
            return;
        }

        string uploadUrl = await _graphClient.CreateUploadSessionAsync(token, sender, draftId, attachment, cancellationToken);
        for (int offset = 0; offset < attachment.Content.Length; offset += UploadChunkBytes)
        {
            int count = Math.Min(UploadChunkBytes, attachment.Content.Length - offset);
            await _graphClient.UploadChunkAsync(uploadUrl, attachment.Content, offset, count, cancellationToken);
        }
    }

    private async Task TryDeleteDraftAsync(string token, string sender, string draftId)
    {
        try
        {
            await _graphClient.DeleteMessageAsync(token, sender, draftId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Entwurf {DraftId} im Postfach {Sender} konnte nicht gelöscht werden.", draftId, sender);
        }
    }

    /// <summary>Entfernt alle Anhänge aus der Mail und liefert deren dekodierten Inhalt.</summary>
    private static async Task<List<AttachmentData>> DetachAttachmentsAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        var parts = new List<MimePart>();
        CollectAndRemove(message.Body, parts);

        var result = new List<AttachmentData>(parts.Count);
        foreach (var part in parts)
        {
            using var stream = new MemoryStream();
            await part.Content.DecodeToAsync(stream, cancellationToken);
            result.Add(new AttachmentData(part.FileName ?? DefaultAttachmentName, part.ContentType.MimeType, stream.ToArray()));
        }

        return result;
    }

    private static void CollectAndRemove(MimeEntity entity, List<MimePart> parts)
    {
        if (entity is not Multipart multipart)
        {
            return;
        }

        for (int i = multipart.Count - 1; i >= 0; i--)
        {
            if (multipart[i] is MimePart part && part.IsAttachment)
            {
                parts.Insert(0, part);
                multipart.RemoveAt(i);
            }
            else
            {
                CollectAndRemove(multipart[i], parts);
            }
        }
    }
}
