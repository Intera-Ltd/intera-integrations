using Intera.Common.Infrastructure.Http;
using Intera.Unleashed.Api.Infrastructure;
using Intera.Unleashed.Api.Models;
using Intera.Unleashed.Api.Options;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text;

namespace Intera.Unleashed.Api.Clients;

public interface IUnleashedClient
{
    Task<IReadOnlyList<T>> GetAllAsync<T>(
        string resource,
        IReadOnlyList<KeyValuePair<string, string?>> parameters,
        CancellationToken cancellationToken);
}

/// <summary>
/// Thin client over the Unleashed API. Signs every request and follows pagination; it knows nothing
/// about the query DSL.
/// </summary>
public sealed class UnleashedClient(
    IOptions<UnleashedOptions> options,
    IJsonHttpClient httpClient,
    ILogger<UnleashedClient> logger) : ApiClientBase(httpClient), IUnleashedClient
{
    private UnleashedOptions Options => options.Value;

    public async Task<IReadOnlyList<T>> GetAllAsync<T>(
        string resource,
        IReadOnlyList<KeyValuePair<string, string?>> parameters,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentNullException.ThrowIfNull(parameters);

        ValidateOptions();

        var baseUrl = HttpsUriValidator.RequireHttpsOrLoopback(Options.ApiBaseUrl, "Unleashed:ApiBaseUrl").ToString();
        var pageSize = Math.Clamp(Options.PageSize, 1, 1000);
        var maxPages = Math.Max(1, Options.MaxPages);

        var query = UnleashedRequestSigner.BuildQueryString(
            [new KeyValuePair<string, string?>("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)), .. parameters]);

        var signature = UnleashedRequestSigner.Sign(query, Options.ApiKey);

        var items = new List<T>();

        for (var page = 1; page <= maxPages; page++)
        {
            var url = new StringBuilder(BuildUrl(baseUrl, resource))
                .Append('/')
                .Append(page.ToString(CultureInfo.InvariantCulture))
                .Append('?')
                .Append(query)
                .ToString();

            var response = await SendAsync<UnleashedPage<T>>(url, signature, cancellationToken).ConfigureAwait(false);

            if (response is null || response.Items.Count == 0)
            {
                break;
            }

            items.AddRange(response.Items);

            if (response.Pagination is { NumberOfPages: > 0 } pagination)
            {
                if (page >= pagination.NumberOfPages)
                {
                    return items;
                }
            }
            else if (response.Items.Count < pageSize)
            {
                return items;
            }

            if (page == maxPages)
            {
                logger.LogWarning(
                    "Unleashed {Resource} was truncated at {MaxPages} pages ({Count} records). Narrow the query or raise Unleashed:MaxPages.",
                    resource,
                    maxPages,
                    items.Count);
            }
        }

        return items;
    }

    private async Task<T?> SendAsync<T>(string url, string signature, CancellationToken cancellationToken)
        where T : class
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["api-auth-id"] = Options.ApiId,
            ["api-auth-signature"] = signature,
            ["client-type"] = Options.ClientType
        };

        using var content = new StringContent(string.Empty, Encoding.UTF8, "application/json");

        try
        {
            return await HttpClient
                .SendAsync<T>(HttpMethod.Get, url, content, headers: headers, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpApiException exception)
        {
            throw UnleashedErrors.Translate(exception);
        }
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(Options.ApiBaseUrl))
        {
            throw new InvalidOperationException("Unleashed:ApiBaseUrl is not configured.");
        }

        if (string.IsNullOrWhiteSpace(Options.ApiId))
        {
            throw new UnleashedAuthenticationException("Unleashed:ApiId is not configured.");
        }

        if (string.IsNullOrWhiteSpace(Options.ApiKey))
        {
            throw new UnleashedAuthenticationException("Unleashed:ApiKey is not configured.");
        }

        if (string.IsNullOrWhiteSpace(Options.ClientType))
        {
            throw new UnleashedAuthenticationException("Unleashed:ClientType is not configured, and Unleashed requires the client-type header.");
        }
    }
}
