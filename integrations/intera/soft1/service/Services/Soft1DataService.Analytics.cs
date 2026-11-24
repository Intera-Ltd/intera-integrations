using Intera.Common.Models.Query;
using Intera.Soft1.Api.Models;
using System.Globalization;

namespace Intera.Soft1.Api.Services;

using static Soft1ReadCatalog;
using Row = Dictionary<string, object?>;

/// <remarks>
/// <para>
/// The analytic sources are sums over the document sources, computed here rather than left to the host because
/// the host can only group what one source returns, and these join two (invoices and orders, documents and their
/// lines). They count <b>invoices</b> — documents whose series is neither an order nor a delivery note, so a
/// delivery note later invoiced is not counted twice — and not cancelled ones, at their <b>net</b> amount in the
/// document's own currency; <c>currency</c> is therefore a grouping key, never summed across.
/// </para>
/// <para>
/// Two things are not available and read null rather than being estimated: <b>gross margin and cost</b> (a
/// document carries no cost; the item movement cost in <c>MTRTRN</c> is per stock movement and would count a
/// delivery note's cost against its invoice twice), and a <b>credit note's sign</b> — a credit note is a document
/// of its own type with a positive amount, and which types credit is not something the browser shows. On an
/// installation that issues credit notes the sales amounts are therefore gross of them; the transaction sources
/// carry the correct sign.
/// </para>
/// </remarks>
public sealed partial class Soft1DataService
{
    /// <summary>
    /// The document window behind a monthly source: a filter on <c>period_start</c> becomes the months it covers.
    /// </summary>
    private (DateOnly? From, DateOnly? To) MonthWindow(Soft1Source source, QueryRequest? query)
    {
        var (from, to) = Window(source, query);

        return (
            from is { } start ? new DateOnly(start.Year, start.Month, 1) : null,
            to is { } end ? new DateOnly(end.Year, end.Month, 1).AddMonths(1).AddDays(-1) : null);
    }

    private static (string Period, DateTime Start)? Month(object? date) =>
        date is DateTime value
            ? (value.ToString("yyyy-MM", CultureInfo.InvariantCulture), new DateTime(value.Year, value.Month, 1))
            : null;

    private async Task<IReadOnlyList<Row>> InvoicesAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var headers = await ReadHeadersAsync(Sales, SeriesKind.Document, from, to, cancellationToken).ConfigureAwait(false);
        return [.. headers.Where(static row => Text(row["document_category"]) == "invoice" && row["cancelled"] as bool? != true)];
    }

    private async Task<IReadOnlyList<Row>> LoadSalesByCustomerAsync(QueryRequest? query, Soft1Source source, CancellationToken cancellationToken)
    {
        var (from, to) = MonthWindow(source, query);
        var invoices = await InvoicesAsync(from, to, cancellationToken).ConfigureAwait(false);

        return
        [
            .. invoices
                .Where(static row => Month(row["document_date"]) is not null)
                .GroupBy(static row => (
                    Month: Month(row["document_date"])!.Value,
                    Customer: Text(row["customer_id"]),
                    Salesperson: Text(row["salesperson_id"]),
                    Currency: Text(row["currency"])))
                .Select(static group => new Row(StringComparer.OrdinalIgnoreCase)
                {
                    ["period"] = group.Key.Month.Period,
                    ["period_start"] = group.Key.Month.Start,
                    ["customer_id"] = group.Key.Customer,
                    ["customer_name"] = group.Select(static row => Text(row["customer_name"])).FirstOrDefault(static name => name is not null),
                    ["salesperson_id"] = group.Key.Salesperson,
                    ["currency"] = group.Key.Currency,
                    ["sales_amount"] = group.Sum(static row => Sum(row["net_amount"])),
                    ["invoice_count"] = (decimal)group.Count()
                })
        ];
    }

    private async Task<IReadOnlyList<Row>> LoadSalesByItemAsync(QueryRequest? query, Soft1Source source, CancellationToken cancellationToken)
    {
        var (from, to) = MonthWindow(source, query);
        var invoices = await InvoicesAsync(from, to, cancellationToken).ConfigureAwait(false);

        var lines = await ReadLinesAsync(
            Sales.Object,
            [
                .. invoices
                    .Where(static row => Text(row["document_id"]) is not null)
                    .Select(static row => (Text(row["document_id"])!, new Row(StringComparer.OrdinalIgnoreCase)
                    {
                        ["document_date"] = row["document_date"],
                        ["currency"] = row["currency"]
                    }))
            ],
            Options.LineLocateInfo,
            "ITELINES",
            MapItemLine,
            cancellationToken).ConfigureAwait(false);

        return
        [
            .. lines
                .Where(static line => Month(line["document_date"]) is not null)
                .GroupBy(static line => (Month: Month(line["document_date"])!.Value, Item: Text(line["item_id"]), Currency: Text(line["currency"])))
                .Select(static group => new Row(StringComparer.OrdinalIgnoreCase)
                {
                    ["period"] = group.Key.Month.Period,
                    ["period_start"] = group.Key.Month.Start,
                    ["item_id"] = group.Key.Item,
                    ["item_code"] = group.Select(static line => Text(line["item_code"])).FirstOrDefault(static code => code is not null),
                    ["description"] = group.Select(static line => Text(line["description"])).FirstOrDefault(static text => text is not null),
                    ["quantity"] = group.Sum(static line => Sum(line["quantity"])),
                    ["sales_amount"] = group.Sum(static line => Sum(line["net_amount"])),
                    ["currency"] = group.Key.Currency
                })
        ];
    }

    private async Task<IReadOnlyList<Row>> LoadSalesBySalespersonAsync(QueryRequest? query, Soft1Source source, CancellationToken cancellationToken)
    {
        var (from, to) = MonthWindow(source, query);

        var invoicesTask = InvoicesAsync(from, to, cancellationToken);
        var ordersTask = ReadHeadersAsync(Sales, SeriesKind.Order, from, to, cancellationToken);
        var peopleTask = LoadSalespeopleAsync(cancellationToken);
        await Task.WhenAll(invoicesTask, ordersTask, peopleTask).ConfigureAwait(false);

        var names = peopleTask.Result
            .Where(static row => Text(row["salesperson_id"]) is not null)
            .GroupBy(static row => Text(row["salesperson_id"])!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => Text(group.First()["name"]), StringComparer.OrdinalIgnoreCase);

        static (string Period, DateTime Start, string? Salesperson, string? Currency)? Key(Row row) =>
            Month(row["document_date"]) is { } month ? (month.Period, month.Start, Text(row["salesperson_id"]), Text(row["currency"])) : null;

        var orders = ordersTask.Result
            .Where(static row => row["cancelled"] as bool? != true && Key(row) is not null)
            .GroupBy(static row => Key(row)!.Value)
            .ToDictionary(static group => group.Key, static group => group.Count());

        var invoices = invoicesTask.Result
            .Where(static row => Key(row) is not null)
            .GroupBy(static row => Key(row)!.Value)
            .ToDictionary(static group => group.Key, static group => group.ToList());

        return
        [
            .. invoices.Keys.Union(orders.Keys).Select(key =>
            {
                var rows = invoices.GetValueOrDefault(key) ?? [];

                return new Row(StringComparer.OrdinalIgnoreCase)
                {
                    ["period"] = key.Period,
                    ["period_start"] = key.Start,
                    ["salesperson_id"] = key.Salesperson,
                    ["salesperson_name"] = key.Salesperson is null ? null : names.GetValueOrDefault(key.Salesperson),
                    ["currency"] = key.Currency,
                    ["sales_amount"] = rows.Sum(static row => Sum(row["net_amount"])),
                    ["customer_count"] = (decimal)rows.Select(static row => Text(row["customer_id"])).Where(static id => id is not null)
                        .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    ["order_count"] = (decimal)orders.GetValueOrDefault(key),
                    ["invoice_count"] = (decimal)rows.Count
                };
            })
        ];
    }

    /// <summary>
    /// Per customer: the credit limit (<c>CRDLIMIT1</c>) against the balance now plus open orders, all in company
    /// currency. Orders in another currency and instalments in another currency are left out of the sums for the
    /// same reason as in the balances: there is no local equivalent to add.
    /// </summary>
    private async Task<IReadOnlyList<Row>> LoadCreditStatusAsync(CancellationToken cancellationToken)
    {
        var customersTask = reader.ReadAsync(CustomersRead, Soft1ReadScope.Everything, cancellationToken);
        var balancesTask = reader.ReadAsync(TraderBalancesRead, Soft1ReadScope.Everything, cancellationToken);
        var ordersTask = ReadHeadersAsync(Sales, SeriesKind.Order, null, null, cancellationToken);
        var openTask = OpenItemsAsync(cancellationToken);
        var currencyTask = CompanyCurrencyAsync(cancellationToken);
        var currencyIdTask = CompanyCurrencyIdAsync(cancellationToken);
        await Task.WhenAll(customersTask, balancesTask, ordersTask, openTask, currencyTask, currencyIdTask).ConfigureAwait(false);

        var balances = balancesTask.Result
            .Where(static row => Text(row["trader_id"]) is not null)
            .GroupBy(static row => Text(row["trader_id"])!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => Number(group.First()["balance_local"]), StringComparer.OrdinalIgnoreCase);

        var openOrders = ordersTask.Result
            .Where(row => Text(row["status"]) is "open" or "partially_converted"
                && (Text(row["currency"]) is null || string.Equals(Text(row["currency"]), currencyTask.Result, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(static row => Text(row["customer_id"]) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Sum(static row => Sum(row["total_amount"])), StringComparer.OrdinalIgnoreCase);

        var today = DateTime.Today;
        var overdue = openTask.Result
            .Where(item => Text(item["currency_id"]) == currencyIdTask.Result && DateValue(item["due_date"]) is { } due && due.Date < today)
            .GroupBy(static item => Text(item["trader_id"]) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                group => (Amount: group.Sum(static item => Sum(item["outstanding_amount"])),
                    Oldest: group.Max(item => (today - DateValue(item["due_date"])!.Value.Date).Days)),
                StringComparer.OrdinalIgnoreCase);

        return
        [
            .. customersTask.Result.Select(customer =>
            {
                var id = Text(customer["customer_id"]) ?? string.Empty;
                var limit = Number(customer["credit_limit"]);
                var balance = balances.GetValueOrDefault(id) ?? Number(customer["balance"]);
                var orders = openOrders.GetValueOrDefault(id);
                var late = overdue.GetValueOrDefault(id);
                var available = limit is > 0 ? limit - (balance ?? 0m) - orders : null;

                return new Row(StringComparer.OrdinalIgnoreCase)
                {
                    ["customer_id"] = customer["customer_id"],
                    ["name"] = customer["name"],
                    ["credit_limit"] = limit,
                    ["current_balance"] = balance,
                    ["open_orders"] = orders,
                    ["available_credit"] = available,
                    ["overdue_amount"] = late.Amount,
                    ["oldest_overdue_days"] = late.Amount > 0 ? (decimal)late.Oldest : null,
                    ["credit_status"] = available switch
                    {
                        null => "no_limit",
                        < 0 => "over_limit",
                        _ when available < limit * 0.1m => "near_limit",
                        _ => "ok"
                    }
                };
            })
        ];
    }
}
