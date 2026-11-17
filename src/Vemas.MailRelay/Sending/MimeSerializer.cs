using MimeKit;

namespace Vemas.MailRelay.Sending;

/// <summary>Laden und Schreiben von MIME sowie Größenberechnung für Graph-Requests.</summary>
public static class MimeSerializer
{
    /// <summary>
    /// Graph begrenzt einen Request auf 4 MB. Der MIME-Inhalt wird Base64-kodiert übertragen (+33 %),
    /// daher wird mit etwas Reserve gerechnet.
    /// </summary>
    public const int MaxGraphRequestBytes = 3_900_000;

    public static async Task<MimeMessage> LoadAsync(byte[] mime, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(mime, writable: false);
        return await MimeMessage.LoadAsync(stream, cancellationToken);
    }

    public static async Task<byte[]> SerializeAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        var options = FormatOptions.Default.Clone();
        options.NewLineFormat = NewLineFormat.Dos;

        using var stream = new MemoryStream();
        await message.WriteToAsync(options, stream, cancellationToken);
        return stream.ToArray();
    }

    public static bool FitsIntoSingleRequest(int mimeLength)
    {
        long base64Length = 4L * ((mimeLength + 2) / 3);
        return base64Length <= MaxGraphRequestBytes;
    }
}
