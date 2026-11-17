using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vemas.MailRelay.Sending;

/// <summary>Einzelne Aufrufe der Microsoft Graph Mail-API (v1.0).</summary>
public sealed class GraphMailClient
{
    public const string GraphClientName = "graph";
    public const string UploadClientName = "graph-upload";
    public const string GraphBaseAddress = "https://graph.microsoft.com/v1.0/";

    private const string RequestIdHeader = "request-id";

    private readonly IHttpClientFactory _httpClientFactory;

    public GraphMailClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Sendet eine Mail im MIME-Format (max. 4 MB Request). Kopie landet in "Gesendete Elemente".</summary>
    public async Task<GraphResponseInfo> SendMimeAsync(string token, string sender, byte[] mime, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, $"users/{Uri.EscapeDataString(sender)}/sendMail", token);
        request.Content = CreateMimeContent(mime);
        using var response = await SendAsync(request, cancellationToken);
        return ToInfo(response);
    }

    /// <summary>Legt aus MIME einen Entwurf im Postfach des Absenders an.</summary>
    public async Task<string> CreateDraftFromMimeAsync(string token, string sender, byte[] mime, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, $"users/{Uri.EscapeDataString(sender)}/messages", token);
        request.Content = CreateMimeContent(mime);
        using var response = await SendAsync(request, cancellationToken);
        var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        return json?["id"]?.GetValue<string>() ?? throw new InvalidOperationException("Graph lieferte keine Entwurfs-ID.");
    }

    /// <summary>Hängt einen Anhang bis ca. 3 MB direkt an einen Entwurf an.</summary>
    public async Task AddAttachmentAsync(string token, string sender, string draftId, AttachmentData attachment, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["@odata.type"] = "#microsoft.graph.fileAttachment",
            ["name"] = attachment.Name,
            ["contentType"] = attachment.ContentType,
            ["contentBytes"] = Convert.ToBase64String(attachment.Content)
        };

        using var request = CreateRequest(HttpMethod.Post, $"users/{Uri.EscapeDataString(sender)}/messages/{draftId}/attachments", token);
        request.Content = JsonContent.Create(body);
        using var response = await SendAsync(request, cancellationToken);
    }

    /// <summary>Erzeugt eine Upload-Session für große Anhänge (bis 150 MB) und liefert die Upload-URL.</summary>
    public async Task<string> CreateUploadSessionAsync(string token, string sender, string draftId, AttachmentData attachment, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["AttachmentItem"] = new JsonObject
            {
                ["attachmentType"] = "file",
                ["name"] = attachment.Name,
                ["size"] = attachment.Content.Length,
                ["contentType"] = attachment.ContentType
            }
        };

        string path = $"users/{Uri.EscapeDataString(sender)}/messages/{draftId}/attachments/createUploadSession";
        using var request = CreateRequest(HttpMethod.Post, path, token);
        request.Content = JsonContent.Create(body);
        using var response = await SendAsync(request, cancellationToken);
        var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        return json?["uploadUrl"]?.GetValue<string>() ?? throw new InvalidOperationException("Graph lieferte keine Upload-URL.");
    }

    /// <summary>Lädt einen Block hoch. Die Upload-URL ist vorab autorisiert (kein Bearer-Token senden).</summary>
    public async Task UploadChunkAsync(string uploadUrl, byte[] content, int offset, int count, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, uploadUrl);
        request.Content = new ByteArrayContent(content, offset, count);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, offset + count - 1, content.Length);
        using var response = await SendAsync(request, cancellationToken, UploadClientName);
    }

    public async Task<GraphResponseInfo> SendDraftAsync(string token, string sender, string draftId, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, $"users/{Uri.EscapeDataString(sender)}/messages/{draftId}/send", token);
        using var response = await SendAsync(request, cancellationToken);
        return ToInfo(response);
    }

    public async Task DeleteMessageAsync(string token, string sender, string messageId, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Delete, $"users/{Uri.EscapeDataString(sender)}/messages/{messageId}", token);
        using var response = await SendAsync(request, cancellationToken);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string relativeUrl, string token)
    {
        var request = new HttpRequestMessage(method, relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static StringContent CreateMimeContent(byte[] mime)
    {
        return new StringContent(Convert.ToBase64String(mime), Encoding.ASCII, "text/plain");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken, string clientName = GraphClientName)
    {
        var client = _httpClientFactory.CreateClient(clientName);
        var response = await client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new GraphRequestException(response.StatusCode, BuildErrorMessage(response, body), GetRequestId(response));
        }
    }

    private static string BuildErrorMessage(HttpResponseMessage response, string body)
    {
        string detail = body;
        try
        {
            var error = JsonNode.Parse(body)?["error"];
            if (error is not null)
            {
                detail = $"{error["code"]?.GetValue<string>()}: {error["message"]?.GetValue<string>()}";
            }
        }
        catch (JsonException)
        {
            detail = body.Length > 500 ? body[..500] : body;
        }

        return $"Graph {(int)response.StatusCode} {response.ReasonPhrase} - {detail}";
    }

    private static GraphResponseInfo ToInfo(HttpResponseMessage response)
    {
        return new GraphResponseInfo((int)response.StatusCode, GetRequestId(response));
    }

    private static string GetRequestId(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues(RequestIdHeader, out var values) ? values.FirstOrDefault() : null;
    }
}
