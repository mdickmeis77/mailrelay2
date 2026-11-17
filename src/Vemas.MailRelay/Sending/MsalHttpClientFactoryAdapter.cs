using Microsoft.Identity.Client;

namespace Vemas.MailRelay.Sending;

/// <summary>Lässt MSAL HttpClients über IHttpClientFactory beziehen (kein eigenes new HttpClient()).</summary>
public sealed class MsalHttpClientFactoryAdapter : IMsalHttpClientFactory
{
    public const string ClientName = "msal";

    private readonly IHttpClientFactory _httpClientFactory;

    public MsalHttpClientFactoryAdapter(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public HttpClient GetHttpClient()
    {
        return _httpClientFactory.CreateClient(ClientName);
    }
}
