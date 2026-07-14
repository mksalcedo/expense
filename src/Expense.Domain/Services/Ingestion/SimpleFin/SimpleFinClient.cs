using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Expense.Domain.Services.Ingestion.SimpleFin;

/// <summary>
/// Plain JSON over REST, per the design doc - no SDK exists or is needed. The access
/// URL SimpleFin issues has Basic Auth credentials embedded in its userinfo component
/// (https://user:pass@host/...); HttpClient does not send those automatically, so
/// they're extracted once here and applied as a real Authorization header on every
/// request, with the credential-free URL used for the actual request line.
/// </summary>
public class SimpleFinClient
{
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;
    private readonly string _basicAuthValue;

    public SimpleFinClient(HttpClient httpClient, string accessUrl)
    {
        _httpClient = httpClient;

        var uri = new Uri(accessUrl);
        var userInfo = Uri.UnescapeDataString(uri.UserInfo);
        _basicAuthValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(userInfo));

        _baseUri = new UriBuilder(uri) { UserName = "", Password = "" }.Uri;
    }

    public async Task<SimpleFinAccountsResponse> GetAccountsAsync(DateTimeOffset startDate, CancellationToken cancellationToken = default)
    {
        var baseUrl = _baseUri.ToString().TrimEnd('/');
        var requestUri = $"{baseUrl}/accounts?start-date={startDate.ToUnixTimeSeconds()}";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _basicAuthValue);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<SimpleFinAccountsResponse>(json)
            ?? throw new InvalidOperationException("SimpleFin returned an empty response body.");
    }
}
