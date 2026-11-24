using Intera.Common.Infrastructure.Query;
using Intera.Common.Models.Query;
using Intera.Unleashed.Api.Clients;
using Intera.Unleashed.Api.Models;
using Intera.Unleashed.Api.Models.Query;
using Intera.Unleashed.Api.Options;
using Microsoft.Extensions.Options;

namespace Intera.Unleashed.Api.Services;

public interface IStockOnHandService
{
    /// <summary>Stock on hand at SKU × warehouse grain.</summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(QueryRequest? query, CancellationToken cancellationToken);
}

/// <summary>
/// Reads stock on hand once per warehouse and concatenates the answers.
/// </summary>
/// <remarks>
/// <para>
/// The customer's requirement is "SKU × warehouse quantities", and <c>warehouseCode</c> is what
/// guarantees that grain: a row fetched under an explicit warehouse code is that warehouse's stock,
/// with no question about whether the endpoint would otherwise have aggregated across sites. That is
/// why the fan-out exists even though <c>GET /StockOnHand</c> can be called without a warehouse at
/// all — the un-scoped call's grain is not something the documentation states, and a silently
/// aggregated row would look exactly like a per-warehouse one.
/// </para>
/// <para>
/// The cost is one upstream read per warehouse, which is why a filter naming warehouses is pushed
/// down first: <c>warehouse_code = 'MAIN'</c> is one read, and so is
/// <c>warehouse_name LIKE '%Cold%'</c> when only one site matches, because the warehouse list is
/// cached and the condition is evaluated against it here rather than upstream.
/// </para>
/// </remarks>
public sealed class StockOnHandService(
    IUnleashedClient client,
    ICatalogService catalogService,
    IOptions<UnleashedOptions> options,
    ILogger<StockOnHandService> logger) : IStockOnHandService
{
    /// <summary>The only warehouse identifier the stock endpoint takes.</summary>
    private static readonly string[] KeyFields = ["warehouse_code"];

    /// <summary>
    /// Warehouse fields the cached list already answers, so a condition on any of them narrows the
    /// fan-out without an upstream call. Each is also a result column of this source.
    /// </summary>
    private static readonly string[] LocalFields = ["warehouse_id", "warehouse_code", "warehouse_name"];

    private readonly UnleashedOptions options = options.Value;

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(
        QueryRequest? query,
        CancellationToken cancellationToken)
    {
        var pushdown = QueryPushdownExtractor.Extract(query, dateField: null, KeyFields, LocalFields);

        var warehouses = await catalogService.GetWarehousesAsync(cancellationToken).ConfigureAwait(false);
        var targets = ResolveTargets(warehouses, pushdown);

        if (targets.Count == 0)
        {
            return [];
        }

        if (targets.Count < warehouses.Count)
        {
            logger.LogDebug("Stock on hand narrowed to {Selected} of {Total} warehouses.", targets.Count, warehouses.Count);
        }

        return await WarehouseSelection.FanOutAsync(
            targets,
            options.MaxParallelRequests,
            LoadWarehouseAsync,
            cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<UnleashedWarehouse> ResolveTargets(
        IReadOnlyList<UnleashedWarehouse> warehouses,
        QueryPushdown pushdown)
    {
        var selected = WarehouseSelection.Restrict(warehouses, pushdown);

        var codes = pushdown.Values("warehouse_code");
        if (codes.Count > 0)
        {
            selected =
            [
                .. selected.Where(warehouse =>
                    warehouse.WarehouseCode is not null
                    && codes.Contains(warehouse.WarehouseCode, StringComparer.OrdinalIgnoreCase))
            ];
        }

        if (selected.Count <= options.MaxWarehouses)
        {
            return selected;
        }

        logger.LogWarning(
            "Stock on hand fan-out capped at {Max} of {Count} warehouses (Unleashed:MaxWarehouses).",
            options.MaxWarehouses,
            selected.Count);

        return [.. selected.Take(options.MaxWarehouses)];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LoadWarehouseAsync(
        UnleashedWarehouse warehouse,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(warehouse.WarehouseCode))
        {
            return [];
        }

        var stock = await client.GetAllAsync<UnleashedStockOnHand>(
            "StockOnHand",
            [new KeyValuePair<string, string?>("warehouseCode", warehouse.WarehouseCode)],
            cancellationToken).ConfigureAwait(false);

        return
        [
            .. stock.Select(item => (IReadOnlyDictionary<string, object?>)Describe(item, warehouse))
        ];
    }

    /// <summary>
    /// Fills the warehouse identity from the list rather than trusting the stock row for it. The row
    /// carries a warehouse name and sometimes a code, but the request already named the warehouse, so
    /// the catalog is the authority and every row of one fan-out branch is labelled identically.
    /// </summary>
    private static Dictionary<string, object?> Describe(UnleashedStockOnHand stock, UnleashedWarehouse warehouse)
    {
        var row = UnleashedRows.StockOnHand(stock);

        row["warehouse_id"] = warehouse.Id is null || warehouse.Id.Value == Guid.Empty ? null : warehouse.Id.Value.ToString();
        row["warehouse_code"] = warehouse.WarehouseCode?.Trim();
        row["warehouse_name"] = string.IsNullOrWhiteSpace(warehouse.WarehouseName)
            ? row["warehouse_name"]
            : warehouse.WarehouseName.Trim();

        return row;
    }
}
