using Intera.Common.Infrastructure.Query;
using Intera.Common.Models.Query;
using Intera.ZohoInventory.Api.Clients;
using Intera.ZohoInventory.Api.Infrastructure;
using Intera.ZohoInventory.Api.Models.Query;
using Intera.ZohoInventory.Api.Options;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;

namespace Intera.ZohoInventory.Api.Services;

using Rows = IReadOnlyList<IReadOnlyDictionary<string, object?>>;

public interface IDocumentService
{
    Task<Rows> GetSalesOrderRowsAsync(QueryRequest? query, CancellationToken cancellationToken);

    Task<Rows> GetSalesOrderLineRowsAsync(QueryRequest? query, CancellationToken cancellationToken);

    Task<Rows> GetPurchaseOrderRowsAsync(QueryRequest? query, CancellationToken cancellationToken);

    Task<Rows> GetPurchaseOrderLineRowsAsync(QueryRequest? query, CancellationToken cancellationToken);

    Task<Rows> GetPurchaseReceiveRowsAsync(QueryRequest? query, CancellationToken cancellationToken);

    Task<Rows> GetPurchaseReceiveLineRowsAsync(QueryRequest? query, CancellationToken cancellationToken);

    Task<Rows> GetPackageRowsAsync(QueryRequest? query, CancellationToken cancellationToken);

    Task<Rows> GetPackageLineRowsAsync(QueryRequest? query, CancellationToken cancellationToken);

    Task<Rows> GetShipmentRowsAsync(QueryRequest? query, CancellationToken cancellationToken);

    Task<Rows> GetShipmentLineRowsAsync(QueryRequest? query, CancellationToken cancellationToken);

    Task<Rows> GetInvoiceRowsAsync(QueryRequest? query, CancellationToken cancellationToken);

    Task<Rows> GetInvoiceLineRowsAsync(QueryRequest? query, CancellationToken cancellationToken);

    Task<Rows> GetBillRowsAsync(QueryRequest? query, CancellationToken cancellationToken);

    Task<Rows> GetBillLineRowsAsync(QueryRequest? query, CancellationToken cancellationToken);

    Task<Rows> GetContactPersonRowsAsync(QueryRequest? query, CancellationToken cancellationToken);

    Task<Rows> GetInventoryByItemRowsAsync(QueryRequest? query, CancellationToken cancellationToken);

    Task<Rows> GetInventoryByLocationRowsAsync(QueryRequest? query, CancellationToken cancellationToken);
}

/// <summary>
/// Reads Zoho's documents — sales orders, purchase orders, purchase receives, packages, invoices,
/// bills — as header rows (one list read) or as line rows (a list read, then one detail call per
/// document); and, by the same list-narrow-fan-out path, the reads that are not documents: contact
/// persons (one call per customer) and stock (one call per batch of items).
/// </summary>
/// <remarks>
/// <para>
/// <b>A line query costs one Zoho call per document it reads</b>, against a daily allowance of a few
/// thousand calls for the whole organization, and nothing here caches or syncs persistently.
/// What keeps a query's cost proportional to its question is the order of work: the list is read
/// first (200 documents per call), the filter's conditions on header fields are evaluated against it,
/// and only the documents that can still match are read in detail.
/// <c>customer_name LIKE '%Acme%'</c> over a thousand orders costs five list calls and one detail
/// call per matching order, not a thousand.
/// </para>
/// <para>
/// The narrowing is the same superset rule as every other pushdown here: only the filter's top-level
/// AND chain is used, a condition is only evaluated against a header field that holds <b>the same
/// value</b> the line row repeats, and the full query still runs over the line rows afterwards. A
/// header field the list is not documented to carry is never used — a missing value would read as
/// null and drop documents that match.
/// </para>
/// <para>
/// <b>Upstream parameters are used only where Zoho documents them, and only in the form measured to
/// work.</b> The package and purchase receive lists document date and customer/vendor filters, so
/// those are sent — the dates only as a complete pair, because a lone bound is capped at today,
/// refused or ignored (see <see cref="ReadListAsync"/>). The sales order and purchase order lists
/// document nothing but paging; they very likely accept the Zoho Books filters too, but a parameter
/// Zoho silently ignored would be harmless while one it read differently would lose rows.
/// </para>
/// <para>
/// A query that would still read more than <c>ZohoInventory:MaxDocumentsPerQuery</c> documents is
/// refused with a 400 naming the fields that would narrow it, rather than truncated — see
/// <see cref="ZohoQueryTooBroadException"/>.
/// </para>
/// </remarks>
public sealed class DocumentService(
    IZohoInventoryClient client,
    IOptions<ZohoInventoryOptions> options,
    ILogger<DocumentService> logger) : IDocumentService
{
    /// <summary>Row key carrying a header's list position through the narrowing engine.</summary>
    private const string IndexKey = "__document_index";

    private static readonly DocumentSpec SalesOrders = new(
        "salesorders", "salesorders", "salesorder", "salesorder_id", ZohoRows.SalesOrder, ZohoQueryFields.SalesOrders);

    private static readonly DocumentSpec PurchaseOrders = new(
        "purchaseorders", "purchaseorders", "purchaseorder", "purchaseorder_id", ZohoRows.PurchaseOrder, ZohoQueryFields.PurchaseOrders);

    private static readonly DocumentSpec PurchaseReceives = new(
        "purchasereceives", "purchasereceives", "purchasereceive", "purchasereceive_id", ZohoRows.PurchaseReceive, ZohoQueryFields.PurchaseReceives);

    private static readonly DocumentSpec Packages = new(
        "packages", "packages", "package", "package_id", ZohoRows.Package, ZohoQueryFields.Packages);

    private static readonly DocumentSpec Invoices = new(
        "invoices", "invoices", "invoice", "invoice_id", ZohoRows.Invoice, ZohoQueryFields.Invoices);

    private static readonly DocumentSpec Bills = new(
        "bills", "bills", "bill", "bill_id", ZohoRows.Bill, ZohoQueryFields.Bills);

    /// <summary>Customers, read as the owners of contact persons; see <see cref="ContactPersons"/>.</summary>
    private static readonly DocumentSpec Customers = new(
        "contacts", "contacts", "contact", "contact_id", ZohoRows.Customer, ZohoQueryFields.Customers);

    /// <summary>Items, read as the owners of stock; see <see cref="InventoryItems"/>.</summary>
    private static readonly DocumentSpec Items = new(
        "items", "items", "item", "item_id", ZohoRows.Item, ZohoQueryFields.Items);

    private static readonly KeyValuePair<string, string>[] AllStatuses = [new("filter_by", "Status.All")];

    private static readonly SourceSpec SalesOrderHeaders = new(SalesOrders);

    private static readonly SourceSpec SalesOrderLines = new(SalesOrders)
    {
        HeaderFields = Map(
            ("salesorder_id", "salesorder_id"),
            ("salesorder_number", "salesorder_number"),
            ("customer_id", "customer_id"),
            ("customer_name", "customer_name"),
            ("order_date", "date"),
            ("shipment_date", "shipment_date"),
            ("order_status", "status"),
            ("reference_number", "reference_number"),
            ("salesperson_name", "salesperson_name"),
            ("created_time", "created_time"),
            ("last_modified_time", "last_modified_time"))
    };

    private static readonly SourceSpec PurchaseOrderHeaders = new(PurchaseOrders);

    private static readonly SourceSpec PurchaseOrderLines = new(PurchaseOrders)
    {
        HeaderFields = Map(
            ("purchaseorder_id", "purchaseorder_id"),
            ("purchaseorder_number", "purchaseorder_number"),
            ("vendor_id", "vendor_id"),
            ("vendor_name", "vendor_name"),
            ("order_date", "date"),
            ("delivery_date", "delivery_date"),
            ("expected_delivery_date", "expected_delivery_date"),
            ("po_status", "status"),
            ("reference_number", "reference_number"),
            ("created_time", "created_time"),
            ("last_modified_time", "last_modified_time"))
    };

    private static readonly SourceSpec PurchaseReceiveHeaders = new(PurchaseReceives)
    {
        DateField = "date",
        DateStartParameter = "receive_date_start",
        DateEndParameter = "receive_date_end",
        KeyParameters = Map(("vendor_id", "vendor_id"), ("purchaseorder_number", "purchaseorder_number"))
    };

    private static readonly SourceSpec PurchaseReceiveLines = PurchaseReceiveHeaders with
    {
        HeaderFields = Map(
            ("receive_id", "receive_id"),
            ("receive_number", "receive_number"),
            ("date", "date"),
            ("status", "status"),
            ("purchaseorder_id", "purchaseorder_id"),
            ("purchaseorder_number", "purchaseorder_number"),
            ("vendor_id", "vendor_id"),
            ("vendor_name", "vendor_name"),
            ("created_time", "created_time"),
            ("last_modified_time", "last_modified_time"))
    };

    private static readonly SourceSpec PackageHeaders = new(Packages)
    {
        DateField = "date",
        DateStartParameter = "date_start",
        DateEndParameter = "date_end",
        KeyParameters = Map(("customer_id", "customer_id"))
    };

    private static readonly SourceSpec PackageLines = PackageHeaders with
    {
        DateField = "package_date",
        HeaderFields = Map(
            ("package_id", "package_id"),
            ("package_number", "package_number"),
            ("package_date", "date"),
            ("salesorder_id", "salesorder_id"),
            ("salesorder_number", "salesorder_number"),
            ("customer_id", "customer_id"),
            ("customer_name", "customer_name"),
            ("created_time", "created_time"),
            ("last_modified_time", "last_modified_time"))
    };

    /// <summary>
    /// Shipment lines are package lines of shipped packages, so they narrow like package lines except
    /// that the date a shipment question asks about is the shipment's, which the package list filters
    /// by directly.
    /// </summary>
    private static readonly SourceSpec ShipmentLines = PackageLines with
    {
        DateField = "shipment_date",
        DateStartParameter = "shipment_date_start",
        DateEndParameter = "shipment_date_end",
        SkipUnshipped = true
    };

    private static readonly SourceSpec ShipmentHeaders = PackageHeaders with
    {
        DateField = "shipment_date",
        DateStartParameter = "shipment_date_start",
        DateEndParameter = "shipment_date_end",
        HeaderFields = Map(
            ("salesorder_id", "salesorder_id"),
            ("salesorder_number", "salesorder_number"),
            ("customer_id", "customer_id"),
            ("customer_name", "customer_name")),
        SkipUnshipped = true
    };

    /// <remarks>
    /// Only <c>customer_id</c> goes upstream. The list also documents <c>date</c> (with variants whose
    /// names the documentation garbles) and <c>status</c>, whose values include virtual states such as
    /// <c>unpaid</c> that are not what a row's <c>status</c> holds; neither could be measured, since
    /// the test organization has no invoices.
    /// </remarks>
    private static readonly SourceSpec InvoiceHeaders = new(Invoices)
    {
        KeyParameters = Map(("customer_id", "customer_id"))
    };

    private static readonly SourceSpec InvoiceLines = InvoiceHeaders with
    {
        HeaderFields = Map(
            ("invoice_id", "invoice_id"),
            ("invoice_number", "invoice_number"),
            ("customer_id", "customer_id"),
            ("customer_name", "customer_name"),
            ("invoice_date", "date"),
            ("due_date", "due_date"),
            ("invoice_status", "status"),
            ("reference_number", "reference_number"),
            ("salesperson_id", "salesperson_id"),
            ("salesperson_name", "salesperson_name"),
            ("created_time", "created_time"),
            ("last_modified_time", "last_modified_time"))
    };

    /// <summary>The bill list documents no parameter but paging.</summary>
    private static readonly SourceSpec BillHeaders = new(Bills);

    private static readonly SourceSpec BillLines = new(Bills)
    {
        HeaderFields = Map(
            ("bill_id", "bill_id"),
            ("bill_number", "bill_number"),
            ("bill_date", "date"),
            ("due_date", "due_date"),
            ("bill_status", "status"),
            ("vendor_id", "vendor_id"),
            ("vendor_name", "vendor_name"),
            ("reference_number", "reference_number"),
            ("created_time", "created_time"),
            ("last_modified_time", "last_modified_time"))
    };

    /// <summary>
    /// Contact persons of customers. <c>/contacts</c> holds vendors too and documents no type
    /// parameter, so the type is kept here — before narrowing, so the cap counts customers only.
    /// </summary>
    private static readonly SourceSpec ContactPersons = new(Customers)
    {
        FixedParameters = AllStatuses,
        Keep = static contact => string.Equals(ZohoJson.Text(contact, "contact_type"), "customer", StringComparison.OrdinalIgnoreCase),
        HeaderFields = Map(
            ("customer_id", "customer_id"),
            ("customer_name", "customer_name"),
            ("company_name", "company_name"),
            ("customer_status", "status"))
    };

    /// <summary>
    /// Items whose stock is read. An item that explicitly does not track inventory — a service — has
    /// no stock to report and is not read; one whose flag is absent is.
    /// </summary>
    private static readonly SourceSpec InventoryItems = new(Items)
    {
        FixedParameters = AllStatuses,
        Keep = static item => ZohoJson.Bool(item, "track_inventory") is not false,
        HeaderFields = Map(
            ("item_id", "item_id"),
            ("item_name", "item_name"),
            ("sku", "sku"),
            ("unit", "unit"),
            ("status", "status"),
            ("item_type", "item_type"),
            ("product_type", "product_type"),
            ("category_name", "category_name"),
            ("brand", "brand"),
            ("last_modified_time", "last_modified_time"))
    };

    private readonly ZohoInventoryOptions options = options.Value;

    public Task<Rows> GetSalesOrderRowsAsync(QueryRequest? query, CancellationToken cancellationToken) =>
        HeadersAsync(SalesOrderHeaders, query, cancellationToken);

    public Task<Rows> GetSalesOrderLineRowsAsync(QueryRequest? query, CancellationToken cancellationToken) =>
        LinesAsync(SalesOrderLines, query, ZohoRows.SalesOrderLines, cancellationToken);

    public Task<Rows> GetPurchaseOrderRowsAsync(QueryRequest? query, CancellationToken cancellationToken) =>
        HeadersAsync(PurchaseOrderHeaders, query, cancellationToken);

    public Task<Rows> GetPurchaseOrderLineRowsAsync(QueryRequest? query, CancellationToken cancellationToken) =>
        LinesAsync(PurchaseOrderLines, query, ZohoRows.PurchaseOrderLines, cancellationToken);

    public Task<Rows> GetPurchaseReceiveRowsAsync(QueryRequest? query, CancellationToken cancellationToken) =>
        HeadersAsync(PurchaseReceiveHeaders, query, cancellationToken);

    public Task<Rows> GetPurchaseReceiveLineRowsAsync(QueryRequest? query, CancellationToken cancellationToken) =>
        LinesAsync(PurchaseReceiveLines, query, ZohoRows.PurchaseReceiveLines, cancellationToken);

    public Task<Rows> GetPackageRowsAsync(QueryRequest? query, CancellationToken cancellationToken) =>
        HeadersAsync(PackageHeaders, query, cancellationToken);

    public Task<Rows> GetPackageLineRowsAsync(QueryRequest? query, CancellationToken cancellationToken) =>
        LinesAsync(PackageLines, query, ZohoRows.PackageLines, cancellationToken);

    public async Task<Rows> GetShipmentLineRowsAsync(QueryRequest? query, CancellationToken cancellationToken)
    {
        var rows = await LinesAsync(ShipmentLines, query, ZohoRows.PackageLines, cancellationToken).ConfigureAwait(false);

        // A package that has not been shipped has no shipment, and its lines did not travel.
        return [.. rows.Where(static row => row.TryGetValue("shipment_id", out var id) && id is not null)];
    }

    public async Task<Rows> GetShipmentRowsAsync(QueryRequest? query, CancellationToken cancellationToken)
    {
        var packages = await DetailsAsync(ShipmentHeaders, query, cancellationToken).ConfigureAwait(false);

        var shipments = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        var order = new List<Dictionary<string, object?>>();

        foreach (var package in packages)
        {
            if (ZohoRows.Shipment(package) is not { } row || row["shipment_id"] is not string shipmentId)
            {
                continue;
            }

            if (shipments.TryGetValue(shipmentId, out var existing))
            {
                ZohoRows.MergePackage(existing, package);
                continue;
            }

            shipments[shipmentId] = row;
            order.Add(row);
        }

        return order;
    }

    public Task<Rows> GetInvoiceRowsAsync(QueryRequest? query, CancellationToken cancellationToken) =>
        HeadersAsync(InvoiceHeaders, query, cancellationToken);

    public Task<Rows> GetInvoiceLineRowsAsync(QueryRequest? query, CancellationToken cancellationToken) =>
        LinesAsync(InvoiceLines, query, ZohoRows.InvoiceLines, cancellationToken);

    public Task<Rows> GetBillRowsAsync(QueryRequest? query, CancellationToken cancellationToken) =>
        HeadersAsync(BillHeaders, query, cancellationToken);

    public Task<Rows> GetBillLineRowsAsync(QueryRequest? query, CancellationToken cancellationToken) =>
        LinesAsync(BillLines, query, ZohoRows.BillLines, cancellationToken);

    /// <summary>
    /// One <c>/contacts/{id}/contactpersons</c> read per selected customer. The <c>contact_persons</c>
    /// embedded in a contact detail would cost the same, but documents no <c>contact_person_id</c> —
    /// the one column a person is identified by.
    /// </summary>
    public async Task<Rows> GetContactPersonRowsAsync(QueryRequest? query, CancellationToken cancellationToken)
    {
        var customers = await SelectAsync(ContactPersons, query, cancellationToken).ConfigureAwait(false);
        EnsureWithinLimit(ContactPersons, customers.Count, "customers");

        var persons = await FanOutAsync(
            customers,
            (customer, token) => client.ListAllAsync(
                $"contacts/{Uri.EscapeDataString(RequireId(ContactPersons, customer))}/contactpersons",
                "contact_persons",
                [],
                token),
            cancellationToken).ConfigureAwait(false);

        return
        [
            .. customers.SelectMany((customer, index) => persons[index]
                .Select(person => (IReadOnlyDictionary<string, object?>)ZohoRows.ContactPerson(customer, person)))
        ];
    }

    public async Task<Rows> GetInventoryByItemRowsAsync(QueryRequest? query, CancellationToken cancellationToken)
    {
        var stock = await ReadStockAsync(query, cancellationToken).ConfigureAwait(false);

        return [.. stock.Select(static pair => (IReadOnlyDictionary<string, object?>)ZohoRows.InventoryByItem(pair.Item, pair.Detail))];
    }

    /// <summary>
    /// One row per item and location, from the <c>locations</c> array <c>/itemdetails</c> documents.
    /// </summary>
    /// <remarks>
    /// <b>Zoho breaks stock down by location only in an organization with multi-warehouse enabled.</b>
    /// Measured 2026-09-18 on the test organization, which has it off: <c>/itemdetails</c> carries no
    /// <c>locations</c> at all, and the per-item location call answers "Please enable multi-warehouse
    /// for your organization". An item without the array is then reported once — at the
    /// organization's only location when there is exactly one, since all its stock is there by
    /// definition, and with a null location otherwise rather than at one guessed among several.
    /// </remarks>
    public async Task<Rows> GetInventoryByLocationRowsAsync(QueryRequest? query, CancellationToken cancellationToken)
    {
        var stock = await ReadStockAsync(query, cancellationToken).ConfigureAwait(false);

        JsonElement? onlyLocation = null;
        if (stock.Any(static pair => !ZohoJson.Array(pair.Detail, "locations").Any()))
        {
            var locations = await client.ListAllAsync("locations", "locations", [], cancellationToken).ConfigureAwait(false);
            onlyLocation = locations.Count == 1 ? locations[0] : null;
        }

        return
        [
            .. stock
                .SelectMany(pair => ZohoRows.InventoryByLocation(pair.Item, pair.Detail, onlyLocation))
                .Select(static row => (IReadOnlyDictionary<string, object?>)row)
        ];
    }

    /// <summary>
    /// The selected items paired with their <c>/itemdetails</c> record, read in batches of
    /// <c>ZohoInventory:ItemDetailsBatchSize</c>. The detail is where <c>committed_stock</c> and the
    /// preferred vendor's id live; the list is what the filter was narrowed against, so the columns it
    /// was narrowed on are taken from the list.
    /// </summary>
    private async Task<IReadOnlyList<(JsonElement Item, JsonElement Detail)>> ReadStockAsync(QueryRequest? query, CancellationToken cancellationToken)
    {
        var items = await SelectAsync(InventoryItems, query, cancellationToken).ConfigureAwait(false);
        var batches = items.Chunk(Math.Max(1, options.ItemDetailsBatchSize)).ToArray();
        EnsureWithinLimit(InventoryItems, batches.Length, "item detail batches");

        var details = await FanOutAsync(
            batches,
            (batch, token) => client.GetCollectionAsync(
                "itemdetails",
                "items",
                [new("item_ids", string.Join(',', batch.Select(static item => RequireId(InventoryItems, item))))],
                token),
            cancellationToken).ConfigureAwait(false);

        var byId = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var detail in details.SelectMany(static batch => batch))
        {
            if (ZohoJson.Text(detail, "item_id") is { } id)
            {
                byId.TryAdd(id, detail);
            }
        }

        // An item the batch did not answer for has no stock to report; zeros from the list would be
        // numbers nobody measured.
        var result = new List<(JsonElement Item, JsonElement Detail)>(items.Count);
        foreach (var item in items)
        {
            if (byId.TryGetValue(RequireId(InventoryItems, item), out var detail))
            {
                result.Add((item, detail));
            }
        }

        return result;
    }

    private async Task<Rows> HeadersAsync(SourceSpec spec, QueryRequest? query, CancellationToken cancellationToken)
    {
        var headers = await ReadListAsync(spec, query, cancellationToken).ConfigureAwait(false);

        return [.. headers.Select(header => (IReadOnlyDictionary<string, object?>)spec.Document.HeaderRow(header))];
    }

    private async Task<Rows> LinesAsync(
        SourceSpec spec,
        QueryRequest? query,
        Func<JsonElement, IEnumerable<Dictionary<string, object?>>> toRows,
        CancellationToken cancellationToken)
    {
        var documents = await DetailsAsync(spec, query, cancellationToken).ConfigureAwait(false);

        return [.. documents.SelectMany(toRows).Select(static row => (IReadOnlyDictionary<string, object?>)row)];
    }

    /// <summary>List, narrow, check the cap, then read each remaining document in detail.</summary>
    private async Task<IReadOnlyList<JsonElement>> DetailsAsync(SourceSpec spec, QueryRequest? query, CancellationToken cancellationToken)
    {
        var selected = await SelectAsync(spec, query, cancellationToken).ConfigureAwait(false);
        EnsureWithinLimit(spec, selected.Count, spec.Document.Resource);

        return await FanOutAsync(
            selected,
            (header, token) => client.GetAsync(
                $"{spec.Document.Resource}/{Uri.EscapeDataString(RequireId(spec, header))}",
                spec.Document.DetailProperty,
                token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The list entries a query can still match: read the list, keep, narrow.</summary>
    private async Task<IReadOnlyList<JsonElement>> SelectAsync(SourceSpec spec, QueryRequest? query, CancellationToken cancellationToken)
    {
        var headers = await ReadListAsync(spec, query, cancellationToken).ConfigureAwait(false);

        if (spec.Keep is { } keep)
        {
            headers = [.. headers.Where(keep)];
        }

        var selected = Narrow(spec, headers, query);

        if (spec.SkipUnshipped)
        {
            // Only an explicit "not_shipped" is skipped: the package list is not documented to carry a
            // status at all, and a package whose status is unknown may well have been shipped.
            selected =
            [
                .. selected.Where(static header =>
                    !string.Equals(ZohoJson.Text(header, "status"), "not_shipped", StringComparison.OrdinalIgnoreCase))
            ];
        }

        if (selected.Count < headers.Count)
        {
            logger.LogDebug(
                "Zoho {Resource}: the filter narrowed the detail reads to {Selected} of {Total} records.",
                spec.Document.Resource,
                selected.Count,
                headers.Count);
        }

        return selected;
    }

    /// <param name="reads">How many upstream calls the fan-out would make.</param>
    /// <param name="what">What those calls read, for the message.</param>
    private void EnsureWithinLimit(SourceSpec spec, int reads, string what)
    {
        var limit = Math.Max(1, options.MaxDocumentsPerQuery);
        if (reads > limit)
        {
            throw new ZohoQueryTooBroadException(
                $"This query would read {reads} Zoho Inventory {what} one by one, "
                + $"over the limit of {limit} (ZohoInventory:MaxDocumentsPerQuery). Narrow it with a condition on "
                + $"{DescribeNarrowingFields(spec)}.");
        }
    }

    /// <summary>One upstream read per input, at most <c>MaxParallelRequests</c> at a time, results in input order.</summary>
    private async Task<TResult[]> FanOutAsync<TInput, TResult>(
        IReadOnlyList<TInput> inputs,
        Func<TInput, CancellationToken, Task<TResult>> read,
        CancellationToken cancellationToken)
    {
        var results = new TResult[inputs.Count];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, inputs.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(options.MaxParallelRequests, 1, 10),
                CancellationToken = cancellationToken
            },
            async (index, token) => results[index] = await read(inputs[index], token).ConfigureAwait(false)).ConfigureAwait(false);

        return results;
    }

    private static string RequireId(SourceSpec spec, JsonElement header)
    {
        return ZohoJson.Text(header, spec.Document.IdProperty)
            ?? throw new ZohoRequestException($"A Zoho Inventory {spec.Document.Resource} list entry has no {spec.Document.IdProperty}.");
    }

    private Task<IReadOnlyList<JsonElement>> ReadListAsync(SourceSpec spec, QueryRequest? query, CancellationToken cancellationToken)
    {
        var pushdown = QueryPushdownExtractor.Extract(
            query,
            spec.DateStartParameter is null ? null : spec.DateField,
            [.. spec.KeyParameters.Keys],
            [.. spec.HeaderFields.Keys]);

        var parameters = new List<KeyValuePair<string, string?>>(
            spec.FixedParameters.Select(static parameter => new KeyValuePair<string, string?>(parameter.Key, parameter.Value)));

        // A date range goes upstream only as a complete, ordered pair. Measured 2026-09-14 on the
        // purchase receive and package lists: a start alone is silently capped at today (a document
        // dated tomorrow drops out), a start alone in the future is refused with 400 "Please check
        // the date range", and an end alone is ignored outright. None of that is a superset, so
        // anything short of both bounds is left to the in-memory pass.
        if (spec.DateStartParameter is not null
            && spec.DateEndParameter is not null
            && pushdown is { From: { } from, To: { } to }
            && from <= to)
        {
            parameters.Add(new(spec.DateStartParameter, FormatDate(from)));
            parameters.Add(new(spec.DateEndParameter, FormatDate(to)));
        }

        foreach (var (field, parameter) in spec.KeyParameters)
        {
            // Single-valued upstream: a comma-joined list would be read as one value that matches
            // nothing, which is the one failure a pushdown must never have.
            parameters.Add(new(parameter, pushdown.SingleValue(field)));
        }

        return client.ListAllAsync(spec.Document.Resource, spec.Document.Collection, parameters, cancellationToken);
    }

    /// <summary>
    /// Evaluates the filter's header-field conditions against the list, reusing
    /// <see cref="InMemoryQueryEngine"/> so the narrowing agrees exactly with the filtering the same
    /// conditions get afterwards — case-insensitive <c>LIKE</c> and relative dates included.
    /// </summary>
    private static IReadOnlyList<JsonElement> Narrow(SourceSpec spec, IReadOnlyList<JsonElement> headers, QueryRequest? query)
    {
        if (spec.HeaderFields.Count == 0 || headers.Count == 0)
        {
            return headers;
        }

        // No date field here, so a date condition on a mapped header field is narrowed like any other.
        // It may also have gone upstream — only a complete range does — and applying a necessary
        // condition twice removes nothing extra.
        var pushdown = QueryPushdownExtractor.Extract(query, dateField: null, [], [.. spec.HeaderFields.Keys]);

        if (pushdown.LocalConditions.Count == 0)
        {
            return headers;
        }

        var rows = new List<IReadOnlyDictionary<string, object?>>(headers.Count);
        for (var index = 0; index < headers.Count; index++)
        {
            var row = spec.Document.HeaderRow(headers[index]);
            row[IndexKey] = index;
            rows.Add(row);
        }

        var narrowing = new QueryRequest
        {
            TimeZone = query?.TimeZone,
            WeekStart = query?.WeekStart,
            Filter = new FilterGroup
            {
                Type = "group",
                Operator = LogicalOperator.And,
                Items =
                [
                    .. pushdown.LocalConditions.Select(condition => new FilterCondition
                    {
                        Type = condition.Type,
                        Field = spec.HeaderFields[condition.Field],
                        Operator = condition.Operator,
                        Value = condition.Value
                    })
                ]
            }
        };

        // The index is not in the allowlist, so it cannot be filtered on — it only rides along.
        var matched = new InMemoryQueryEngine(spec.Document.HeaderAllowlist).Execute(rows, narrowing);

        return
        [
            .. matched
                .Select(row => row.TryGetValue(IndexKey, out var index) && index is int position ? (JsonElement?)headers[position] : null)
                .Where(static header => header is not null)
                .Select(static header => header!.Value)
        ];
    }

    private static string DescribeNarrowingFields(SourceSpec spec)
    {
        var fields = spec.HeaderFields.Keys.ToList();
        if (spec.DateStartParameter is not null && spec.DateField is not null && !fields.Contains(spec.DateField))
        {
            fields.Insert(0, spec.DateField);
        }

        return string.Join(", ", fields);
    }

    private static string? FormatDate(DateOnly? value)
    {
        return value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static IReadOnlyDictionary<string, string> Map(params (string Field, string Target)[] pairs)
    {
        return pairs.ToDictionary(static pair => pair.Field, static pair => pair.Target, StringComparer.OrdinalIgnoreCase);
    }

    /// <param name="Resource">Path segment of the list and of the detail call.</param>
    /// <param name="Collection">Property of the list response holding the records.</param>
    /// <param name="DetailProperty">Property of the detail response holding the record.</param>
    /// <param name="IdProperty">List property carrying the id the detail call takes.</param>
    /// <param name="HeaderRow">Builds the header row from a list entry.</param>
    /// <param name="HeaderAllowlist">Allowlist of the header row, for narrowing.</param>
    private sealed record DocumentSpec(
        string Resource,
        string Collection,
        string DetailProperty,
        string IdProperty,
        Func<JsonElement, Dictionary<string, object?>> HeaderRow,
        IReadOnlyDictionary<string, InMemoryQueryField> HeaderAllowlist);

    /// <summary>How one source reads its document: what it pushes upstream and what it narrows locally.</summary>
    private sealed record SourceSpec(DocumentSpec Document)
    {
        /// <summary>Source field whose bounds become the list's date parameters.</summary>
        public string? DateField { get; init; }

        public string? DateStartParameter { get; init; }

        public string? DateEndParameter { get; init; }

        /// <summary>Source field → documented single-valued list parameter.</summary>
        public IReadOnlyDictionary<string, string> KeyParameters { get; init; } = Map();

        /// <summary>
        /// Source field → header row field holding the same value, for narrowing detail reads. Empty
        /// on a header source, which reads no details.
        /// </summary>
        public IReadOnlyDictionary<string, string> HeaderFields { get; init; } = Map();

        /// <summary>Skip packages the list says were never shipped.</summary>
        public bool SkipUnshipped { get; init; }

        /// <summary>Parameters sent with every list read, whatever the filter.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> FixedParameters { get; init; } = [];

        /// <summary>List entries this source is about at all; the rest are dropped before narrowing.</summary>
        public Func<JsonElement, bool>? Keep { get; init; }
    }
}
