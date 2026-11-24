using Intera.Common.Infrastructure.Query;
using Intera.Common.Models.Query;
using Intera.Unleashed.Api.Models;
using Intera.Unleashed.Api.Models.Query;

namespace Intera.Unleashed.Api.Services;

/// <summary>
/// Turns the pushed-down filter into the set of warehouses a stock-on-hand request has to ask about,
/// and runs the fan-out over them.
/// </summary>
/// <remarks>
/// The warehouse list is one cached call, so every condition on a warehouse field can be evaluated
/// against it locally before anything fans out. A filter such as <c>warehouse_name LIKE '%Cold%'</c>
/// therefore costs one stock call rather than one per warehouse, even though the upstream parameter
/// is a plain code.
/// </remarks>
internal static class WarehouseSelection
{
    /// <summary>Row key carrying the catalog position, used to map filtered rows back to their warehouse.</summary>
    private const string IndexKey = "__warehouse_index";

    /// <summary>
    /// Applies the warehouse-field conditions from the filter's top-level AND chain to the warehouse
    /// list.
    /// </summary>
    /// <remarks>
    /// Reuses <see cref="InMemoryQueryEngine"/> rather than hand-rolling comparisons, so the narrowing
    /// here agrees exactly with the filtering the same conditions get afterwards — case-insensitive
    /// <c>LIKE</c> and the null semantics of <c>!=</c> included.
    /// </remarks>
    public static IReadOnlyList<UnleashedWarehouse> Restrict(
        IReadOnlyList<UnleashedWarehouse> catalog,
        QueryPushdown pushdown)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(pushdown);

        if (pushdown.LocalConditions.Count == 0 || catalog.Count == 0)
        {
            return catalog;
        }

        var rows = new List<IReadOnlyDictionary<string, object?>>(catalog.Count);
        for (var index = 0; index < catalog.Count; index++)
        {
            var row = UnleashedRows.Warehouse(catalog[index]);
            row[IndexKey] = index;
            rows.Add(row);
        }

        var query = new QueryRequest
        {
            Filter = new FilterGroup
            {
                Type = "group",
                Operator = LogicalOperator.And,
                Items = pushdown.LocalConditions
            }
        };

        // The index is not in WarehouseQueryFields, so it cannot be filtered on — it only rides along.
        var matched = new InMemoryQueryEngine(WarehouseQueryFields.All).Execute(rows, query);

        return
        [
            .. matched
                .Select(row => row.TryGetValue(IndexKey, out var index) && index is int position ? catalog[position] : null)
                .Where(static warehouse => warehouse is not null)
                .Select(static warehouse => warehouse!)
        ];
    }

    /// <summary>
    /// Runs <paramref name="action"/> over the warehouses with a bounded degree of parallelism, keeping
    /// the result order stable so <c>top</c> means the same thing from one call to the next.
    /// </summary>
    public static async Task<IReadOnlyList<TResult>> FanOutAsync<TItem, TResult>(
        IReadOnlyList<TItem> items,
        int maxParallelRequests,
        Func<TItem, CancellationToken, Task<IReadOnlyList<TResult>>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(action);

        if (items.Count == 0)
        {
            return [];
        }

        var buckets = new IReadOnlyList<TResult>[items.Count];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, items.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, maxParallelRequests),
                CancellationToken = cancellationToken
            },
            async (index, token) => buckets[index] = await action(items[index], token).ConfigureAwait(false)).ConfigureAwait(false);

        return [.. buckets.Where(static bucket => bucket is not null).SelectMany(static bucket => bucket)];
    }
}
