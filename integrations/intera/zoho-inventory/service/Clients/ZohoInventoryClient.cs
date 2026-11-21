using Intera.Common.Infrastructure.Http;
using Intera.ZohoInventory.Api.Infrastructure;
using Intera.ZohoInventory.Api.Options;
using Intera.ZohoInventory.Api.Services;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Intera.ZohoInventory.Api.Clients;

public interface IZohoInventoryClient
{
    /// <summary>
    /// Reads a list resource whole, following <c>page_context.has_more_page</c>.
    /// </summary>
    /// <param name="resource">Path below the API root, e.g. <c>salesorders</c>.</param>
    /// <param name="collection">The response property holding the records, e.g. <c>salesorders</c>.</param>
    /// <param name="parameters">Query parameters; entries with a blank value are dropped.</param>
    /// <exception cref="ZohoQueryTooBroadException">
    /// More pages remain after <c>ZohoInventory:MaxListPages</c>.
    /// </exception>
    Task<IReadOnlyList<JsonElement>> ListAllAsync(
        string resource,
        string collection,
        IReadOnlyList<KeyValuePair<string, string?>> parameters,
        CancellationToken cancellationToken);

    /// <summary>Reads one record, e.g. <c>salesorders/{id}</c>, and returns the object under <paramref name="property"/>.</summary>
    Task<JsonElement> GetAsync(string path, string property, CancellationToken cancellationToken);

    /// <summary>
    /// One unpaginated call answering with a collection, e.g. <c>itemdetails?item_ids=…</c>, and the
    /// records under <paramref name="collection"/>.
    /// </summary>
    Task<IReadOnlyList<JsonElement>> GetCollectionAsync(
        string path,
        string collection,
        IReadOnlyList<KeyValuePair<string, string?>> parameters,
        CancellationToken cancellationToken);
}

/// <summary>
/// Thin client over the Zoho Inventory API: authorisation, organization scoping, pagination, pacing
/// and error translation. It knows nothing about the query DSL.
/// </summary>
/// <remarks>
/// Records are handed on as <see cref="JsonElement"/> rather than typed DTOs.
/// Zoho's documented types disagree with themselves — <c>total_quantity</c> is a string on a
/// package, <c>delivery_days</c> an integer on a shipment and a string on the sales order that embeds
/// it, <c>billing_address</c> an object on one document and an array on the next — and
/// <c>System.Text.Json</c> throws on the first such mismatch, failing the whole query over a column
/// nobody asked for. <c>ZohoJson</c> reads each value by what it is.
/// </remarks>
public sealed class ZohoInventoryClient(
    IOptions<ZohoInventoryOptions> options,
    IJsonHttpClient httpClient,
    IZohoOAuthService oauthService,
    ZohoRequestThrottle throttle,
    ILogger<ZohoInventoryClient> logger) : IZohoInventoryClient
{
    private ZohoInventoryOptions Options => options.Value;

    public async Task<IReadOnlyList<JsonElement>> ListAllAsync(
        string resource,
        string collection,
        IReadOnlyList<KeyValuePair<string, string?>> parameters,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentNullException.ThrowIfNull(parameters);

        var pageSize = Math.Clamp(Options.PageSize, 1, 200);
        var maxPages = Math.Max(1, Options.MaxListPages);
        var records = new List<JsonElement>();

        for (var page = 1; ; page++)
        {
            var root = await SendAsync(resource, [.. parameters, .. Paging(page, pageSize)], cancellationToken).ConfigureAwait(false);

            records.AddRange(ReadCollection(root, collection));

            if (!HasMorePages(root))
            {
                return records;
            }

            if (page >= maxPages)
            {
                logger.LogWarning(
                    "Zoho {Resource} has more than {MaxPages} pages ({Count} records read); the query was refused.",
                    resource,
                    maxPages,
                    records.Count);

                throw new ZohoQueryTooBroadException(
                    $"Zoho Inventory {resource} has more than {maxPages * pageSize} records for this query "
                    + "(ZohoInventory:MaxListPages). Narrow the filter, or raise the limit on the deployment.");
            }
        }
    }

    public async Task<JsonElement> GetAsync(string path, string property, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(property);

        var root = await SendAsync(path, [], cancellationToken).ConfigureAwait(false);

        if (root.TryGetProperty(property, out var record) && record.ValueKind == JsonValueKind.Object)
        {
            return record;
        }

        // The package detail is documented as an array holding the one package; accept both.
        if (record.ValueKind == JsonValueKind.Array && record.GetArrayLength() > 0 && record[0].ValueKind == JsonValueKind.Object)
        {
            return record[0];
        }

        throw new ZohoRequestException($"Zoho Inventory {path} answered without a '{property}' object.");
    }

    public async Task<IReadOnlyList<JsonElement>> GetCollectionAsync(
        string path,
        string collection,
        IReadOnlyList<KeyValuePair<string, string?>> parameters,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentNullException.ThrowIfNull(parameters);

        var root = await SendAsync(path, parameters, cancellationToken).ConfigureAwait(false);

        return ReadCollection(root, collection);
    }

    private async Task<JsonElement> SendAsync(
        string path,
        IReadOnlyList<KeyValuePair<string, string?>> parameters,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Options.OrganizationId))
        {
            throw new ZohoAuthenticationException("ZohoInventory:OrganizationId is not configured.");
        }

        var query = BuildQueryString([new KeyValuePair<string, string?>("organization_id", Options.OrganizationId), .. parameters]);

        // One retry after dropping the cached token, and only one: a token can stop working before its
        // stated expiry (revoked, or the refresh token re-issued elsewhere), but a second 401 means the
        // credential itself is wrong, and retrying further would spend the refresh token's small
        // per-ten-minutes allowance for nothing.
        for (var attempt = 1; ; attempt++)
        {
            var token = await oauthService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            var url = $"{token.ApiBaseUrl}/{path.TrimStart('/')}?{query}";
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Authorization"] = $"Zoho-oauthtoken {token.Token}"
            };

            try
            {
                var root = await throttle.RunAsync(
                    token2 => httpClient.GetAsync<JsonElement>(url, headers: headers, cancellationToken: token2),
                    cancellationToken).ConfigureAwait(false);

                return EnsureSuccess(root, path);
            }
            catch (HttpApiException exception) when (attempt == 1 && ZohoErrors.IsInvalidToken(exception))
            {
                logger.LogInformation("Zoho refused the cached access token for {Path}; acquiring a new one.", path);
                oauthService.Invalidate();
            }
            catch (HttpApiException exception)
            {
                throw ZohoErrors.Translate(exception);
            }
        }
    }

    /// <summary>
    /// Zoho carries its own status in the body as well. A non-zero <c>code</c> under HTTP 200 is still
    /// a failure, and trusting the status alone would read it as an empty answer.
    /// </summary>
    private static JsonElement EnsureSuccess(JsonElement root, string path)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ZohoRequestException($"Zoho Inventory {path} answered with something other than a JSON object.");
        }

        var (code, message) = ZohoErrors.Read(root);
        if (code is not null and not 0)
        {
            throw new ZohoRequestException($"Zoho Inventory {path} answered code {code}: {message ?? "no message"}");
        }

        return root;
    }

    /// <remarks>
    /// Falls back to the singular spelling: the package list is documented as answering under
    /// <c>package</c> while every other list uses the plural, and reading neither would look exactly
    /// like an empty list.
    /// </remarks>
    private static IReadOnlyList<JsonElement> ReadCollection(JsonElement root, string collection)
    {
        if ((!root.TryGetProperty(collection, out var items) || items.ValueKind != JsonValueKind.Array)
            && (!collection.EndsWith('s') || !root.TryGetProperty(collection[..^1], out items) || items.ValueKind != JsonValueKind.Array))
        {
            return [];
        }

        return [.. items.EnumerateArray().Where(static item => item.ValueKind == JsonValueKind.Object)];
    }

    /// <summary>
    /// A response without <c>page_context</c> is not paginated at all (locations are one such), so its
    /// absence means "this was everything".
    /// </summary>
    private static bool HasMorePages(JsonElement root)
    {
        return root.TryGetProperty("page_context", out var context)
            && context.ValueKind == JsonValueKind.Object
            && context.TryGetProperty("has_more_page", out var hasMore)
            && hasMore.ValueKind == JsonValueKind.True;
    }

    private static KeyValuePair<string, string?>[] Paging(int page, int perPage)
    {
        return
        [
            new("page", page.ToString(CultureInfo.InvariantCulture)),
            new("per_page", perPage.ToString(CultureInfo.InvariantCulture))
        ];
    }

    private static string BuildQueryString(IEnumerable<KeyValuePair<string, string?>> parameters)
    {
        var builder = new StringBuilder();

        foreach (var (name, value) in parameters)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('&');
            }

            builder.Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(value));
        }

        return builder.ToString();
    }
}
