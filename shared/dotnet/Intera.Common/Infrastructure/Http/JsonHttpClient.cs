using System.Net.Http.Headers;
using System.Text.Json;

namespace Intera.Common.Infrastructure.Http;

public sealed class JsonHttpClient(HttpClient httpClient) : IJsonHttpClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<TResponse?> GetAsync<TResponse>(
        string url,
        string? bearerToken = null,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<TResponse>(HttpMethod.Get, url, null, bearerToken, headers, cancellationToken);
    }

    public Task<TResponse?> PostFormAsync<TResponse>(
        string url,
        IReadOnlyDictionary<string, string> form,
        string? bearerToken = null,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<TResponse>(
            HttpMethod.Post,
            url,
            new FormUrlEncodedContent(form),
            bearerToken,
            headers,
            cancellationToken);
    }

    public async Task<TResponse?> SendAsync<TResponse>(
        HttpMethod method,
        string url,
        HttpContent? content = null,
        string? bearerToken = null,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(method, url)
        {
            Content = content
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        if (headers is not null)
        {
            foreach (var (key, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(key, value);
            }
        }

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpApiException(response.StatusCode, responseBody);
        }

        if (typeof(TResponse) == typeof(string))
        {
            return (TResponse)(object)responseBody;
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return default;
        }

        return JsonSerializer.Deserialize<TResponse>(responseBody, JsonOptions);
    }
}
