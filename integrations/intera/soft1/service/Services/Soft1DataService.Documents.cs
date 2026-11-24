using Intera.Common.Infrastructure.Query;
using Intera.Common.Models.Query;
using Intera.Soft1.Api.Infrastructure;
using Intera.Soft1.Api.Models;
using System.Globalization;
using System.Text.Json;

namespace Intera.Soft1.Api.Services;

using static Soft1ReadCatalog;
using Row = Dictionary<string, object?>;

public sealed partial class Soft1DataService
{
    private enum SeriesKind
    {
        /// <summary>Every document that is not an order: invoices, credit notes, delivery notes.</summary>
        Document,

        Order,

        /// <summary>Delivery notes only — shipments on the sales side, goods receipts on the purchase side.</summary>
        Delivery
    }

    /// <param name="Module">SOSOURCE: 1351 sales documents, 1251 purchase documents.</param>
    /// <param name="Object">The business object <c>getData</c> reads one document through.</param>
    /// <param name="Trader">Row key prefix of the document's trader: <c>customer</c> or <c>supplier</c>.</param>
    private sealed record DocumentModule(int Module, string HeadersRead, string Object, string Trader, int TraderEntity);

    private static readonly DocumentModule Sales = new(1351, SalesHeadersRead, "SALDOC", "customer", CustomerEntity);

    private static readonly DocumentModule Purchases = new(1251, PurchaseHeadersRead, "PURDOC", "supplier", SupplierEntity);

    private sealed record SeriesInfo(string Id, string? Name, string? TypeId, string? TypeName, string Category);

    /// <summary>
    /// The document series of one module and what each one is: an order, a delivery note or anything else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A default document browser shows neither the series nor the document type, so the series is how a row is
    /// told apart: the browser is read once per series (its <c>SERIES</c> dialog filter is SoftOne's own, from the
    /// GO API), and the series decides the rest. Inferred, not confirmed: <c>TFPRMS</c> 201 is an order (BlackBook's
    /// own example is <c>SALDOC.TFPRMS&lt;&gt;201</c>, "not an order"), and <c>FPRMS.ISDELIVERYNOTE</c> marks a
    /// delivery note. The order behaviours are configurable (<c>Soft1:OrderBehaviours</c>) for that reason.
    /// </para>
    /// <para>
    /// A module with no readable series is refused rather than read unclassified: every order would otherwise
    /// count as an invoice.
    /// </para>
    /// </remarks>
    private Task<Dictionary<string, SeriesInfo>> ClassifySeries(DocumentModule module, CancellationToken cancellationToken) =>
        Memo($"series-{module.Module}", async () =>
        {
            var seriesTask = reader.ReadAsync(SeriesRead, Soft1ReadScope.Everything, cancellationToken);
            var typesTask = reader.ReadAsync(DocumentTypesRead, Soft1ReadScope.Everything, cancellationToken);
            await Task.WhenAll(seriesTask, typesTask).ConfigureAwait(false);

            var types = typesTask.Result
                .Where(type => Entity(type["module"]) == module.Module && Text(type["document_type_id"]) is not null)
                .GroupBy(static type => Text(type["document_type_id"])!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

            var orders = Options.OrderBehaviours is { Length: > 0 } behaviours ? behaviours.ToHashSet() : [201];
            var result = new Dictionary<string, SeriesInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var series in seriesTask.Result.Where(series => Entity(series["module"]) == module.Module))
            {
                if (Text(series["series_id"]) is not { } id)
                {
                    continue;
                }

                var typeId = Text(series["document_type_id"]);
                var type = typeId is not null ? types.GetValueOrDefault(typeId) : null;
                var behaviour = Entity(series["behaviour"]) ?? Entity(type?["behaviour"]);

                var category = behaviour is { } value && orders.Contains(value)
                    ? "order"
                    : type?["is_delivery_note"] as bool? == true ? "delivery_note" : "invoice";

                result[id] = new SeriesInfo(id, Text(series["name"]), typeId, Text(type?["name"]), category);
            }

            if (result.Count == 0)
            {
                throw new Soft1RequestException(
                    $"No document series of module {module.Module} could be read from Soft1 (table SERIES), so orders cannot be " +
                    "told from invoices. Check GET /api/Schema/reads/series/probe.");
            }

            return result;
        });

    private (DateOnly? From, DateOnly? To) Window(Soft1Source source, QueryRequest? query)
    {
        if (source.DateField is null)
        {
            return (null, null);
        }

        var pushdown = QueryPushdownExtractor.Extract(query, source.DateField, []);
        return (pushdown.From, pushdown.To);
    }

    /// <summary>
    /// The fiscal years a window spans — the whole of each, since the tables are keyed by year. No window reads
    /// the <c>Soft1:DefaultFiscalYears</c> most recent. Fiscal years are taken to be calendar years.
    /// </summary>
    private IReadOnlyList<int> FiscalYears(DateOnly? from, DateOnly? to)
    {
        var today = DateTime.Today.Year;
        var last = Math.Min(to?.Year ?? today, Math.Max(today, from?.Year ?? today));
        var first = from?.Year ?? last - Math.Max(1, Options.DefaultFiscalYears) + 1;

        if (first > last)
        {
            return [];
        }

        if (last - first + 1 > Math.Max(1, Options.MaxFiscalYears))
        {
            throw new Soft1QueryTooBroadException(
                $"The date range spans {last - first + 1} fiscal years ({first}-{last}), more than Soft1:MaxFiscalYears " +
                $"({Options.MaxFiscalYears}). Narrow the date filter.");
        }

        return [.. Enumerable.Range(first, last - first + 1)];
    }

    private async Task<IReadOnlyList<Row>> ReadHeadersAsync(
        DocumentModule module,
        SeriesKind kind,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var series = await ClassifySeries(module, cancellationToken).ConfigureAwait(false);

        var wanted = series.Values
            .Where(info => kind switch
            {
                SeriesKind.Order => info.Category == "order",
                SeriesKind.Delivery => info.Category == "delivery_note",
                _ => info.Category != "order"
            })
            .Select(static info => info.Id)
            .ToList();

        if (wanted.Count == 0)
        {
            return [];
        }

        var rows = await reader.ReadAsync(module.HeadersRead, new Soft1ReadScope(from, to, Series: wanted), cancellationToken)
            .ConfigureAwait(false);

        var traderId = $"{module.Trader}_id";
        var traderCode = $"{module.Trader}_code";
        Dictionary<string, string>? idsByCode = null;

        foreach (var row in rows)
        {
            var info = Text(row["series_id"]) is { } id ? series.GetValueOrDefault(id) : null;
            row["series"] = info?.Name ?? Text(row["series_id"]);
            row["document_type"] = info?.TypeName ?? info?.Name;
            row["document_type_id"] = info?.TypeId;
            row["document_category"] = info?.Category;
            row["status"] = DocumentStatus(row);

            // A default document browser names the trader by code and name (TRDR_CUSTOMER_CODE), not always by id —
            // and the id is what every other source joins on.
            if (row[traderId] is null && Text(row[traderCode]) is { } code)
            {
                idsByCode ??= await TraderIdsByCodeAsync(module.TraderEntity, cancellationToken).ConfigureAwait(false);
                row[traderId] = idsByCode.GetValueOrDefault(code);
            }
        }

        // Read per series, every row already belongs to the kind asked for. With the series filter switched off
        // (Soft1:Reads:<read>:SeriesFilter empty) the browser returns every document at once, and only a series
        // column in the browser can still sort them — a row without one is no order and no delivery note anyone
        // can prove, so it counts as a document.
        return
        [
            .. rows.Where(row => kind switch
            {
                SeriesKind.Order => Text(row["document_category"]) == "order",
                SeriesKind.Delivery => Text(row["document_category"]) == "delivery_note",
                _ => Text(row["document_category"]) != "order"
            })
        ];
    }

    private async Task<Dictionary<string, string>> TraderIdsByCodeAsync(int entity, CancellationToken cancellationToken)
    {
        var traders = await TradersAsync(cancellationToken).ConfigureAwait(false);

        return traders.Values
            .Where(trader => trader.Entity == entity && trader.Code is not null)
            .GroupBy(static trader => trader.Code!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().Id, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <c>cancelled</c>, else what <c>FULLYTRANSF</c> says about conversion. Inferred from SoftOne's own samples
    /// (<c>FULLYTRANSF IN (1,2)</c> for completed orders, <c>IN (0,2)</c> for convertible lines): 0 open, 1 fully
    /// converted, 2 partially.
    /// </summary>
    private static string DocumentStatus(Row row)
    {
        if (row.GetValueOrDefault("cancelled") as bool? == true)
        {
            return "cancelled";
        }

        return Text(row.GetValueOrDefault("fully_transformed")) switch
        {
            "1" => "converted",
            "2" => "partially_converted",
            _ => "open"
        };
    }

    private async Task<IReadOnlyList<Row>> LoadDocumentHeadersAsync(
        DocumentModule module,
        SeriesKind kind,
        QueryRequest? query,
        Soft1Source source,
        CancellationToken cancellationToken)
    {
        var (from, to) = Window(source, query);
        return await ReadHeadersAsync(module, kind, from, to, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<Row>> LoadOrdersAsync(DocumentModule module, QueryRequest? query, Soft1Source source, CancellationToken cancellationToken)
    {
        var (from, to) = Window(source, query);
        var rows = await ReadHeadersAsync(module, SeriesKind.Order, from, to, cancellationToken).ConfigureAwait(false);

        foreach (var row in rows)
        {
            row["order_id"] = row["document_id"];
            row["order_number"] = row["document_number"];
            row["order_date"] = row["document_date"];
            row[$"{module.Trader}_reference"] = row["reference"];
        }

        return rows;
    }

    private async Task<IReadOnlyList<Row>> LoadDeliveriesAsync(DocumentModule module, QueryRequest? query, Soft1Source source, CancellationToken cancellationToken)
    {
        var (from, to) = Window(source, query);
        var rows = await ReadHeadersAsync(module, SeriesKind.Delivery, from, to, cancellationToken).ConfigureAwait(false);
        var prefix = module == Sales ? "shipment" : "receipt";

        foreach (var row in rows)
        {
            row[$"{prefix}_id"] = row["document_id"];
            row[$"{prefix}_number"] = row["document_number"];
            row[$"{prefix}_date"] = row["document_date"];
        }

        return rows;
    }

    // ---- Lines ------------------------------------------------------------------------------------------------

    /// <summary>
    /// The fields a line row repeats from its document, under the names the line source uses. They are also what
    /// the filter can narrow the documents by before any of them is read line by line.
    /// </summary>
    private static Row LineHeader(DocumentModule module, SeriesKind kind, Row header)
    {
        var (id, number, date) = kind switch
        {
            SeriesKind.Order => ("order_id", "order_number", "order_date"),
            SeriesKind.Delivery when module == Sales => ("shipment_id", "shipment_number", "shipment_date"),
            SeriesKind.Delivery => ("receipt_id", "receipt_number", "receipt_date"),
            _ => ("document_id", "document_number", "document_date")
        };

        var traderId = $"{module.Trader}_id";

        return new Row(StringComparer.OrdinalIgnoreCase)
        {
            [id] = header["document_id"],
            [number] = header["document_number"],
            [date] = header["document_date"],
            [traderId] = header[traderId],
            ["currency"] = header["currency"]
        };
    }

    private async Task<IReadOnlyList<Row>> LoadDocumentLinesAsync(
        DocumentModule module,
        SeriesKind kind,
        QueryRequest? query,
        Soft1Source source,
        CancellationToken cancellationToken)
    {
        var (from, to) = Window(source, query);
        var headers = await ReadHeadersAsync(module, kind, from, to, cancellationToken).ConfigureAwait(false);

        var documents = headers
            .Where(static header => header["cancelled"] as bool? != true)
            .Select(header => (Key: Text(header["document_id"]), Header: LineHeader(module, kind, header)))
            .Where(static document => document.Key is not null)
            .ToList();

        var selected = Narrow(source, query, documents.Select(static document => document.Header).ToList());
        var keep = selected.ToHashSet();

        return await ReadLinesAsync(
            module.Object,
            documents.Where(document => keep.Contains(document.Header)).Select(static document => (document.Key!, document.Header)).ToList(),
            Options.LineLocateInfo,
            "ITELINES",
            MapItemLine,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<Row>> LoadVatAsync(QueryRequest? query, Soft1Source source, CancellationToken cancellationToken)
    {
        var (from, to) = Window(source, query);
        var salesTask = ReadHeadersAsync(Sales, SeriesKind.Document, from, to, cancellationToken);
        var purchasesTask = ReadHeadersAsync(Purchases, SeriesKind.Document, from, to, cancellationToken);
        await Task.WhenAll(salesTask, purchasesTask).ConfigureAwait(false);

        static Row Header(DocumentModule module, Row header) => new(StringComparer.OrdinalIgnoreCase)
        {
            ["document_id"] = header["document_id"],
            ["document_number"] = header["document_number"],
            ["document_date"] = header["document_date"],
            ["direction"] = module == Sales ? "sales" : "purchases",
            [$"{module.Trader}_id"] = header[$"{module.Trader}_id"],
            ["currency"] = header["currency"],
            ["period"] = header["document_date"] is DateTime date ? date.ToString("yyyy-MM", CultureInfo.InvariantCulture) : null,
            ["__object"] = module.Object
        };

        // Invoices only: a delivery note carries no VAT of its own, and one later invoiced would be counted twice.
        static bool Taxable(Row header) =>
            Text(header["document_category"]) == "invoice" && header["cancelled"] as bool? != true;

        var documents = salesTask.Result.Where(Taxable).Select(header => Header(Sales, header))
            .Concat(purchasesTask.Result.Where(Taxable).Select(header => Header(Purchases, header)))
            .Where(static header => Text(header["document_id"]) is not null)
            .ToList();

        var selected = Narrow(source, query, documents).ToHashSet();
        var rows = new List<Row>();

        foreach (var group in documents.Where(selected.Contains).GroupBy(static header => (string)header["__object"]!))
        {
            rows.AddRange(await ReadLinesAsync(
                group.Key,
                [.. group.Select(static header => (Text(header["document_id"])!, header))],
                Options.VatLocateInfo,
                "VATANAL",
                static (line, header) =>
                {
                    var row = new Row(header, StringComparer.OrdinalIgnoreCase)
                    {
                        ["transaction_id"] = $"{Text(header["document_id"])}-{Field(line, "LINENUM", Soft1ValueKind.Text) ?? Field(line, "VAT", Soft1ValueKind.Id)}",
                        ["vat_code"] = Field(line, "VAT", Soft1ValueKind.Label) ?? Field(line, "VAT", Soft1ValueKind.Id),
                        ["vat_rate"] = Field(line, "VAT_VAT_PERCNT", Soft1ValueKind.Number),
                        ["taxable_amount"] = Field(line, "SUBVAL", Soft1ValueKind.Number),
                        ["vat_amount"] = Field(line, "VATVAL", Soft1ValueKind.Number)
                    };
                    return row;
                },
                cancellationToken).ConfigureAwait(false));
        }

        return rows;
    }

    /// <summary>
    /// Keeps the documents the filter can still match, judged by the fields a line row repeats from its document.
    /// The same superset rule as every pushdown here: only the filter's top-level AND chain is used, and the full
    /// query still runs over the line rows afterwards.
    /// </summary>
    private static IReadOnlyList<Row> Narrow(Soft1Source source, QueryRequest? query, IReadOnlyList<Row> headers)
    {
        if (headers.Count == 0)
        {
            return headers;
        }

        var headerFields = headers[0].Keys.Where(key => source.Allowlist.ContainsKey(key)).ToList();
        var pushdown = QueryPushdownExtractor.Extract(query, dateField: null, [], headerFields);

        if (pushdown.LocalConditions.Count == 0)
        {
            return headers;
        }

        var narrowing = new QueryRequest
        {
            TimeZone = query?.TimeZone,
            WeekStart = query?.WeekStart,
            Filter = new FilterGroup
            {
                Type = "group",
                Operator = LogicalOperator.And,
                Items = [.. pushdown.LocalConditions]
            }
        };

        // Rows come back as the same instances, so the caller can match them by reference.
        return [.. new InMemoryQueryEngine(source.Allowlist).Execute(headers, narrowing).OfType<Row>()];
    }

    /// <summary>
    /// One <c>getData</c> per document, restricted by <paramref name="locateInfo"/>, expanded into a row per line
    /// of <paramref name="table"/>. More documents than <c>Soft1:MaxRecordReadsPerQuery</c> is refused first.
    /// </summary>
    private async Task<IReadOnlyList<Row>> ReadLinesAsync(
        string obj,
        IReadOnlyList<(string Key, Row Header)> documents,
        string locateInfo,
        string table,
        Func<JsonElement, Row, Row> mapLine,
        CancellationToken cancellationToken)
    {
        var limit = Math.Max(1, Options.MaxRecordReadsPerQuery);
        if (documents.Count > limit)
        {
            throw new Soft1QueryTooBroadException(
                $"This query would read {documents.Count} Soft1 documents one call each, more than Soft1:MaxRecordReadsPerQuery ({limit}). " +
                "Narrow it by date, document or customer/supplier.");
        }

        logger.LogInformation("Reading lines of {Count} Soft1 {Object} documents.", documents.Count, obj);

        var parts = await Task.WhenAll(documents.Select(async document =>
        {
            var data = await client.GetRecordAsync(obj, document.Key, locateInfo, cancellationToken).ConfigureAwait(false);

            if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(table, out var lines) || lines.ValueKind != JsonValueKind.Array)
            {
                return new List<Row>();
            }

            return lines.EnumerateArray()
                .Where(static line => line.ValueKind == JsonValueKind.Object)
                .Select(line => mapLine(line, document.Header))
                .ToList();
        })).ConfigureAwait(false);

        return [.. parts.SelectMany(static part => part)];
    }

    /// <summary>One item line (<c>ITELINES</c>, i.e. <c>MTRLINES</c>) under every name the six line sources use.</summary>
    private static Row MapItemLine(JsonElement line, Row header)
    {
        var quantity = Field(line, "QTY1", Soft1ValueKind.Number) as decimal?;
        var converted = Field(line, "QTY1COV", Soft1ValueKind.Number) as decimal?;
        var cancelled = Field(line, "QTY1CANC", Soft1ValueKind.Number) as decimal?;
        var net = (Field(line, "NETLINEVAL", Soft1ValueKind.Number) ?? Field(line, "LINEVAL", Soft1ValueKind.Number)) as decimal?;
        var vat = Field(line, "VATAMNT", Soft1ValueKind.Number) as decimal?;
        var total = net is null && vat is null ? (decimal?)null : (net ?? 0m) + (vat ?? 0m);
        var price = Field(line, "PRICE", Soft1ValueKind.Number);
        var delivery = Field(line, "DELIVDATE", Soft1ValueKind.Date);

        return new Row(header, StringComparer.OrdinalIgnoreCase)
        {
            ["line_id"] = Field(line, "MTRLINES", Soft1ValueKind.Id),
            ["line_number"] = Field(line, "LINENUM", Soft1ValueKind.Number),
            ["item_id"] = Field(line, "MTRL", Soft1ValueKind.Id),
            ["item_code"] = Field(line, "MTRL_ITEM_CODE", Soft1ValueKind.Text),
            ["description"] = Field(line, "MTRL_ITEM_NAME", Soft1ValueKind.Text) ?? Field(line, "COMMENTS", Soft1ValueKind.Text),
            ["quantity"] = quantity,
            ["quantity_received"] = quantity,
            ["quantity_shipped"] = quantity,
            ["ordered_qty"] = quantity,
            ["delivered_qty"] = converted,
            ["received_qty"] = converted,
            ["cancelled_qty"] = cancelled,
            ["open_qty"] = quantity is { } ordered ? Math.Max(0m, ordered - (converted ?? 0m) - (cancelled ?? 0m)) : null,
            ["unit"] = Field(line, "MTRUNIT", Soft1ValueKind.Label) ?? Field(line, "MTRUNIT", Soft1ValueKind.Id),
            ["unit_price"] = price,
            ["unit_cost"] = price,
            ["discount"] = Field(line, "DISC1PRC", Soft1ValueKind.Number),
            ["discount_amount"] = Field(line, "DISC1VAL", Soft1ValueKind.Number),
            ["net_amount"] = net,
            ["vat_amount"] = vat,
            ["total_amount"] = total,
            ["line_total"] = total,
            ["warehouse_id"] = Field(line, "WHOUSE", Soft1ValueKind.Id),
            ["project_id"] = Field(line, "PRJC", Soft1ValueKind.Id),
            ["delivery_date"] = delivery,
            ["requested_date"] = delivery,
            ["expected_date"] = delivery
        };
    }

    private static object? Field(JsonElement line, string name, Soft1ValueKind kind) =>
        line.TryGetProperty(name, out var value) ? Soft1Reader.Convert(Soft1Values.Text(value), kind) : null;
}
