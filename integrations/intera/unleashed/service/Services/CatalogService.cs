using Intera.Common.Infrastructure.Query;
using Intera.Common.Models.Query;
using Intera.Unleashed.Api.Clients;
using Intera.Unleashed.Api.Models;
using Intera.Unleashed.Api.Models.Query;
using Intera.Unleashed.Api.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace Intera.Unleashed.Api.Services;

public interface ICatalogService
{
    /// <summary>
    /// The whole warehouse list, cached. Read by the warehouse endpoint and by every stock-on-hand
    /// query, which needs it to know what to fan out over.
    /// </summary>
    Task<IReadOnlyList<UnleashedWarehouse>> GetWarehousesAsync(CancellationToken cancellationToken);

    /// <summary>The warehouse list as query-DSL rows.</summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetWarehouseRowsAsync(CancellationToken cancellationToken);

    /// <summary>Product master data as query-DSL rows, narrowed upstream by whatever the filter allows.</summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetProductRowsAsync(QueryRequest? query, CancellationToken cancellationToken);
}

/// <summary>
/// The two reference lists — warehouses and products — that the movement sources are read against.
/// </summary>
public sealed class CatalogService(
    IUnleashedClient client,
    IMemoryCache cache,
    IOptions<UnleashedOptions> options,
    ILogger<CatalogService> logger) : ICatalogService
{
    private const string WarehouseCacheKey = "unleashed-warehouses";

    /// <summary>
    /// Product filters Unleashed can enforce itself. <c>productCode</c> matches on a prefix upstream,
    /// which is a superset of the equality it comes from — exactly what a pushdown is allowed to be —
    /// and <c>productId</c> is the one parameter here that takes a list.
    /// </summary>
    private static readonly string[] ProductKeyFields = ["product_code", "product_id"];

    private readonly UnleashedOptions options = options.Value;

    public async Task<IReadOnlyList<UnleashedWarehouse>> GetWarehousesAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue<IReadOnlyList<UnleashedWarehouse>>(WarehouseCacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        // Always the whole list, never a filtered one: it is also the fan-out set for stock on hand,
        // and a cache holding the answer to one query would be the wrong answer to the next.
        // includeObsolete is on so `is_obsolete` is a column a caller can filter on rather than a
        // decision made for them here — a closed site still holds the stock it held.
        var warehouses = await client.GetAllAsync<UnleashedWarehouse>(
            "Warehouses",
            [new KeyValuePair<string, string?>("includeObsolete", "true")],
            cancellationToken).ConfigureAwait(false);

        if (options.CatalogCacheSeconds > 0)
        {
            cache.Set(WarehouseCacheKey, warehouses, TimeSpan.FromSeconds(options.CatalogCacheSeconds));
        }

        logger.LogDebug("Loaded {Count} Unleashed warehouses.", warehouses.Count);

        return warehouses;
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetWarehouseRowsAsync(CancellationToken cancellationToken)
    {
        var warehouses = await GetWarehousesAsync(cancellationToken).ConfigureAwait(false);

        return [.. warehouses.Select(warehouse => (IReadOnlyDictionary<string, object?>)UnleashedRows.Warehouse(warehouse))];
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetProductRowsAsync(
        QueryRequest? query,
        CancellationToken cancellationToken)
    {
        var pushdown = QueryPushdownExtractor.Extract(query, "last_modified_on", ProductKeyFields);

        var parameters = new List<KeyValuePair<string, string?>>
        {
            new("includeObsolete", "true"),
            new("modifiedSince", FormatDate(pushdown.From)),
            // Single-valued upstream, so it is only sent when the filter names exactly one code.
            // Sending "A,B" would be read as one literal code and would answer with nothing.
            new("productCode", pushdown.SingleValue("product_code")),
            new("productId", JoinIds(pushdown.Values("product_id")))
        };

        var products = await client.GetAllAsync<UnleashedProduct>("Products", parameters, cancellationToken).ConfigureAwait(false);

        return [.. products.Select(product => (IReadOnlyDictionary<string, object?>)UnleashedRows.Product(product))];
    }

    /// <summary>
    /// <c>productId</c> is documented as taking a comma-separated list of GUIDs. Values that are not
    /// GUIDs are dropped rather than sent — a malformed id would narrow the fetch to nothing, and the
    /// filter still runs in full afterwards, so dropping one costs a wider fetch and never a row.
    /// </summary>
    private static string? JoinIds(IReadOnlyList<string> values)
    {
        var ids = values.Where(static value => Guid.TryParse(value, out _)).ToArray();

        return ids.Length == 0 ? null : string.Join(',', ids);
    }

    private static string? FormatDate(DateOnly? value)
    {
        return value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
