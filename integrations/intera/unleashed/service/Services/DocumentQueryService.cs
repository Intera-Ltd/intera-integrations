using Intera.Common.Infrastructure.Query;
using Intera.Common.Models.Query;
using Intera.Unleashed.Api.Clients;
using Intera.Unleashed.Api.Models;
using Intera.Unleashed.Api.Models.Query;
using System.Globalization;

namespace Intera.Unleashed.Api.Services;

public interface IDocumentQueryService
{
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetPurchaseOrderLineRowsAsync(QueryRequest? query, CancellationToken cancellationToken);

    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetSalesOrderLineRowsAsync(QueryRequest? query, CancellationToken cancellationToken);

    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetSalesShipmentLineRowsAsync(QueryRequest? query, CancellationToken cancellationToken);

    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetStockTransferLineRowsAsync(QueryRequest? query, CancellationToken cancellationToken);

    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetStockCountLineRowsAsync(QueryRequest? query, CancellationToken cancellationToken);

    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetSupplierReturnLineRowsAsync(QueryRequest? query, CancellationToken cancellationToken);
}

/// <summary>
/// Reads Unleashed's six document resources and flattens each document into one row per line.
/// </summary>
/// <remarks>
/// <para>
/// All six work the same way: whatever of the filter Unleashed can enforce is turned into query-string
/// parameters, the matching documents are read whole (up to <c>Unleashed:MaxPages</c>), and the lines
/// are unnested. The full <see cref="QueryRequest"/> is then evaluated over those rows by
/// <c>InMemoryQueryEngine</c>, so the pushdown is a pure optimisation and never the thing that decides
/// which rows come back.
/// </para>
/// <para>
/// <b>Which conditions are pushed down is decided by what Unleashed documents, not by what would be
/// useful.</b> A pushdown may only ever fetch a superset: narrow it by one row too many and rows go
/// missing with nothing to show for it. So <c>startDate</c>/<c>endDate</c> is used only where the
/// upstream documentation names the column it filters, <c>completedAfter</c>/<c>completedBefore</c>
/// carries the date filter on stock counts (there is no column here for the "created" date its
/// <c>startDate</c> refers to), and warehouse stock transfers — which have no date parameter at all —
/// push <c>modifiedSince</c> instead. Sales orders are the one place where the documentation says
/// "orders with date before" without naming the field; it is read as <c>OrderDate</c>, the object's
/// only such column, and that is the single upstream assumption in this project that a live account
/// should confirm.
/// </para>
/// <para>
/// A single-valued upstream parameter is sent only when the filter names exactly one value. Joining
/// two warehouse codes with a comma would be read upstream as one code that exists nowhere, and the
/// query would answer with nothing at all — the failure mode a pushdown must never have. Only the
/// four parameters Unleashed documents as comma-separated take a list.
/// </para>
/// <para>
/// <c>top</c> and <c>skip</c> are deliberately not pushed down. Unleashed pages <b>documents</b> while
/// these sources answer in <b>lines</b>, so "rows 200-299" cannot be turned into a page number; paging
/// happens in memory, where the whole filtered set is known and <c>X-Total-Count</c> is free.
/// </para>
/// </remarks>
public sealed class DocumentQueryService(IUnleashedClient client) : IDocumentQueryService
{
    private static readonly DocumentSpec PurchaseOrders = new(
        "PurchaseOrders",
        // Documented as filtering OrderDate outright.
        DateField: "order_date",
        StartParameter: "startDate",
        EndParameter: "endDate",
        Keys:
        [
            new PushdownKey("warehouse_code", "warehouseCode"),
            new PushdownKey("supplier_code", "supplierCode"),
            new PushdownKey("order_status", "orderStatus", MultiValue: true),
            new PushdownKey("custom_order_status", "customOrderStatus", MultiValue: true),
            new PushdownKey("order_number", "orderNumber")
        ]);

    private static readonly DocumentSpec SalesOrders = new(
        "SalesOrders",
        // "Orders with date before the specified date" — read as OrderDate. See the class remarks.
        DateField: "order_date",
        StartParameter: "startDate",
        EndParameter: "endDate",
        Keys:
        [
            new PushdownKey("warehouse_code", "warehouseCode"),
            new PushdownKey("customer_code", "customerCode"),
            new PushdownKey("order_status", "orderStatus", MultiValue: true),
            new PushdownKey("custom_order_status", "customOrderStatus", MultiValue: true),
            new PushdownKey("order_number", "orderNumber")
        ]);

    private static readonly DocumentSpec SalesShipments = new(
        "SalesShipments",
        // Documented as filtering the creation date, so it is created_on that is pushed — not
        // dispatch_date, which is the more natural column to ask about and simply is not this filter.
        DateField: "created_on",
        StartParameter: "startDate",
        EndParameter: "endDate",
        Keys:
        [
            new PushdownKey("warehouse_code", "warehouseCode"),
            new PushdownKey("shipment_status", "shipmentStatus"),
            new PushdownKey("order_number", "orderNumber")
        ]);

    private static readonly DocumentSpec StockTransfers = new(
        "WarehouseStockTransfers",
        // The one resource with no date filter of its own; modifiedSince is all there is.
        DateField: "last_modified_on",
        StartParameter: "modifiedSince",
        EndParameter: null,
        Keys: [new PushdownKey("transfer_status", "transferStatus", MultiValue: true)]);

    private static readonly DocumentSpec StockCounts = new(
        "StockCounts",
        // completedAfter/Before name their column; startDate/endDate refer to a creation date this
        // object does not expose, so they are left alone.
        DateField: "completed_date",
        StartParameter: "completedAfter",
        EndParameter: "completedBefore",
        Keys: [new PushdownKey("warehouse_code", "warehouseCode")]);

    private static readonly DocumentSpec SupplierReturns = new(
        "SupplierReturns",
        DateField: "created_on",
        StartParameter: "startDate",
        EndParameter: "endDate",
        Keys:
        [
            new PushdownKey("supplier_code", "supplierCode"),
            new PushdownKey("status", "supplierReturnStatus"),
            new PushdownKey("purchase_order_number", "purchaseOrderNumber"),
            new PushdownKey("supplier_return_number", "supplierReturnNumber")
        ]);

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetPurchaseOrderLineRowsAsync(QueryRequest? query, CancellationToken cancellationToken)
    {
        return QueryAsync<UnleashedPurchaseOrder>(PurchaseOrders, query, UnleashedRows.PurchaseOrderLines, cancellationToken);
    }

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetSalesOrderLineRowsAsync(QueryRequest? query, CancellationToken cancellationToken)
    {
        return QueryAsync<UnleashedSalesOrder>(SalesOrders, query, UnleashedRows.SalesOrderLines, cancellationToken);
    }

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetSalesShipmentLineRowsAsync(QueryRequest? query, CancellationToken cancellationToken)
    {
        return QueryAsync<UnleashedSalesShipment>(SalesShipments, query, UnleashedRows.SalesShipmentLines, cancellationToken);
    }

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetStockTransferLineRowsAsync(QueryRequest? query, CancellationToken cancellationToken)
    {
        return QueryAsync<UnleashedStockTransfer>(StockTransfers, query, UnleashedRows.StockTransferLines, cancellationToken);
    }

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetStockCountLineRowsAsync(QueryRequest? query, CancellationToken cancellationToken)
    {
        return QueryAsync<UnleashedStockCount>(StockCounts, query, UnleashedRows.StockCountLines, cancellationToken);
    }

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetSupplierReturnLineRowsAsync(QueryRequest? query, CancellationToken cancellationToken)
    {
        return QueryAsync<UnleashedSupplierReturn>(SupplierReturns, query, UnleashedRows.SupplierReturnLines, cancellationToken);
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync<TDocument>(
        DocumentSpec spec,
        QueryRequest? query,
        Func<TDocument, IEnumerable<Dictionary<string, object?>>> toRows,
        CancellationToken cancellationToken)
    {
        var pushdown = QueryPushdownExtractor.Extract(
            query,
            spec.DateField,
            [.. spec.Keys.Select(static key => key.Field)]);

        var parameters = new List<KeyValuePair<string, string?>>(spec.Keys.Count + 2);

        if (spec.StartParameter is not null)
        {
            parameters.Add(new KeyValuePair<string, string?>(spec.StartParameter, FormatDate(pushdown.From)));
        }

        if (spec.EndParameter is not null)
        {
            parameters.Add(new KeyValuePair<string, string?>(spec.EndParameter, FormatDate(pushdown.To)));
        }

        foreach (var key in spec.Keys)
        {
            var value = key.MultiValue
                ? Join(pushdown.Values(key.Field))
                : pushdown.SingleValue(key.Field);

            parameters.Add(new KeyValuePair<string, string?>(key.Parameter, value));
        }

        var documents = await client.GetAllAsync<TDocument>(spec.Resource, parameters, cancellationToken).ConfigureAwait(false);

        return [.. documents.SelectMany(toRows).Select(static row => (IReadOnlyDictionary<string, object?>)row)];
    }

    private static string? Join(IReadOnlyList<string> values)
    {
        return values.Count == 0 ? null : string.Join(',', values);
    }

    private static string? FormatDate(DateOnly? value)
    {
        return value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <param name="Resource">Unleashed resource path segment.</param>
    /// <param name="DateField">Row field whose bounds map onto the upstream window, if any.</param>
    /// <param name="StartParameter">Parameter carrying the lower bound.</param>
    /// <param name="EndParameter">Parameter carrying the upper bound, where the resource has one.</param>
    /// <param name="Keys">Entity filters the resource can enforce itself.</param>
    private sealed record DocumentSpec(
        string Resource,
        string? DateField,
        string? StartParameter,
        string? EndParameter,
        IReadOnlyList<PushdownKey> Keys);

    /// <param name="Field">Row field the condition names.</param>
    /// <param name="Parameter">Upstream query-string parameter it maps to.</param>
    /// <param name="MultiValue">
    /// Whether Unleashed documents this parameter as accepting a comma-separated list. False means a
    /// list is dropped rather than joined, because a joined list would be read as one literal value.
    /// </param>
    private sealed record PushdownKey(string Field, string Parameter, bool MultiValue = false);
}
