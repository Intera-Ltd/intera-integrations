using Intera.Common.Infrastructure.Query;
using Intera.Common.Models.Query;
using Intera.Soft1.Api.Clients;
using Intera.Soft1.Api.Infrastructure;
using Intera.Soft1.Api.Models;
using Intera.Soft1.Api.Options;
using Microsoft.Extensions.Options;

namespace Intera.Soft1.Api.Services;

using static Soft1ReadCatalog;
using Row = Dictionary<string, object?>;
using Rows = IReadOnlyList<IReadOnlyDictionary<string, object?>>;

public interface ISoft1DataService
{
    /// <summary>The rows of one data source, narrowed by what the query lets Soft1 narrow.</summary>
    Task<Rows> LoadAsync(Soft1Source source, QueryRequest? query, CancellationToken cancellationToken);
}

/// <summary>
/// Builds the rows of every data source out of the reads in <see cref="Soft1ReadCatalog"/>: projecting them,
/// joining reference data onto them, reading document lines one document at a time, and summing the analytic
/// sources in memory.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything here was written from Soft1 documentation.</b> The rules that are inferred are kept in
/// one place each, so the first live installation corrects them there: the debit/credit sign of a posting
/// (<see cref="SignedValue"/>), what makes a document an order or a delivery note (<see cref="ClassifySeries"/>),
/// and what a document's <c>FULLYTRANSF</c> means (<see cref="DocumentStatus"/>).
/// </para>
/// <para>
/// Scoped: the reference reads one query needs more than once (currencies, transaction types, traders) are kept
/// for the length of the request and no longer. Nothing is cached across requests.
/// </para>
/// </remarks>
public sealed partial class Soft1DataService(
    ISoft1Reader reader,
    ISoft1Client client,
    IOptions<Soft1Options> options,
    ILogger<Soft1DataService> logger) : ISoft1DataService
{
    private readonly Lock memoGate = new();
    private readonly Dictionary<string, object> memo = new(StringComparer.Ordinal);

    private Soft1Options Options => options.Value;

    public async Task<Rows> LoadAsync(Soft1Source source, QueryRequest? query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        IEnumerable<IReadOnlyDictionary<string, object?>> rows = source.Id switch
        {
            Soft1Sources.Customers => await LoadTradersAsync(CustomersRead, true, cancellationToken).ConfigureAwait(false),
            Soft1Sources.Suppliers => await LoadTradersAsync(SuppliersRead, false, cancellationToken).ConfigureAwait(false),
            Soft1Sources.Items => await LoadItemsAsync(cancellationToken).ConfigureAwait(false),
            Soft1Sources.Projects => await LoadProjectsAsync(cancellationToken).ConfigureAwait(false),
            Soft1Sources.Warehouses => await reader.ReadAsync(WarehousesRead, Soft1ReadScope.Everything, cancellationToken).ConfigureAwait(false),
            Soft1Sources.Branches => await reader.ReadAsync(BranchesRead, Soft1ReadScope.Everything, cancellationToken).ConfigureAwait(false),
            Soft1Sources.Companies => await LoadCompaniesAsync(cancellationToken).ConfigureAwait(false),
            Soft1Sources.Currencies => await reader.ReadAsync(CurrenciesRead, Soft1ReadScope.Everything, cancellationToken).ConfigureAwait(false),
            Soft1Sources.PaymentTerms => await LoadPaymentTermsAsync(cancellationToken).ConfigureAwait(false),
            Soft1Sources.TaxCodes => await LoadTaxCodesAsync(cancellationToken).ConfigureAwait(false),
            Soft1Sources.Units => await reader.ReadAsync(UnitsRead, Soft1ReadScope.Everything, cancellationToken).ConfigureAwait(false),
            Soft1Sources.ItemCategories => await LoadCategoriesAsync(ItemCategoriesRead, 51, cancellationToken).ConfigureAwait(false),
            Soft1Sources.CustomerCategories => await LoadCategoriesAsync(TraderCategoriesRead, 13, cancellationToken).ConfigureAwait(false),
            Soft1Sources.SupplierCategories => await LoadCategoriesAsync(TraderCategoriesRead, 12, cancellationToken).ConfigureAwait(false),
            Soft1Sources.DocumentTypes => await LoadDocumentTypesAsync(cancellationToken).ConfigureAwait(false),
            Soft1Sources.Salespeople => await LoadSalespeopleAsync(cancellationToken).ConfigureAwait(false),
            Soft1Sources.ChartOfAccounts => await LoadAccountsAsync(cancellationToken).ConfigureAwait(false),

            Soft1Sources.SalesDocuments => await LoadDocumentHeadersAsync(Sales, SeriesKind.Document, query, source, cancellationToken).ConfigureAwait(false),
            Soft1Sources.PurchaseDocuments => await LoadDocumentHeadersAsync(Purchases, SeriesKind.Document, query, source, cancellationToken).ConfigureAwait(false),
            Soft1Sources.SalesOrders => await LoadOrdersAsync(Sales, query, source, cancellationToken).ConfigureAwait(false),
            Soft1Sources.PurchaseOrders => await LoadOrdersAsync(Purchases, query, source, cancellationToken).ConfigureAwait(false),
            Soft1Sources.Shipments => await LoadDeliveriesAsync(Sales, query, source, cancellationToken).ConfigureAwait(false),
            Soft1Sources.GoodsReceipts => await LoadDeliveriesAsync(Purchases, query, source, cancellationToken).ConfigureAwait(false),

            Soft1Sources.SalesDocumentLines => await LoadDocumentLinesAsync(Sales, SeriesKind.Document, query, source, cancellationToken).ConfigureAwait(false),
            Soft1Sources.PurchaseDocumentLines => await LoadDocumentLinesAsync(Purchases, SeriesKind.Document, query, source, cancellationToken).ConfigureAwait(false),
            Soft1Sources.SalesOrderLines => await LoadDocumentLinesAsync(Sales, SeriesKind.Order, query, source, cancellationToken).ConfigureAwait(false),
            Soft1Sources.PurchaseOrderLines => await LoadDocumentLinesAsync(Purchases, SeriesKind.Order, query, source, cancellationToken).ConfigureAwait(false),
            Soft1Sources.ShipmentLines => await LoadDocumentLinesAsync(Sales, SeriesKind.Delivery, query, source, cancellationToken).ConfigureAwait(false),
            Soft1Sources.GoodsReceiptLines => await LoadDocumentLinesAsync(Purchases, SeriesKind.Delivery, query, source, cancellationToken).ConfigureAwait(false),
            Soft1Sources.VatTransactions => await LoadVatAsync(query, source, cancellationToken).ConfigureAwait(false),

            Soft1Sources.CustomerTransactions => await LoadTraderTransactionsAsync(CustomerEntity, query, source, cancellationToken).ConfigureAwait(false),
            Soft1Sources.SupplierTransactions => await LoadTraderTransactionsAsync(SupplierEntity, query, source, cancellationToken).ConfigureAwait(false),
            Soft1Sources.CustomerBalances => await LoadBalancesAsync(CustomerEntity, cancellationToken).ConfigureAwait(false),
            Soft1Sources.SupplierBalances => await LoadBalancesAsync(SupplierEntity, cancellationToken).ConfigureAwait(false),
            Soft1Sources.OpenReceivables => await LoadOpenItemsAsync(CustomerEntity, cancellationToken).ConfigureAwait(false),
            Soft1Sources.OpenPayables => await LoadOpenItemsAsync(SupplierEntity, cancellationToken).ConfigureAwait(false),
            Soft1Sources.ReceivablesAgeing => await LoadAgeingAsync(CustomerEntity, cancellationToken).ConfigureAwait(false),
            Soft1Sources.PayablesAgeing => await LoadAgeingAsync(SupplierEntity, cancellationToken).ConfigureAwait(false),
            Soft1Sources.GeneralLedgerEntries => await LoadLedgerAsync(query, source, cancellationToken).ConfigureAwait(false),
            Soft1Sources.AccountBalances => await LoadAccountBalancesAsync(query, source, cancellationToken).ConfigureAwait(false),
            Soft1Sources.CashBankAccounts => await LoadCashAccountsAsync(cancellationToken).ConfigureAwait(false),
            Soft1Sources.CashBankTransactions => await LoadCashTransactionsAsync(query, source, cancellationToken).ConfigureAwait(false),
            Soft1Sources.StockBalances => await LoadStockBalancesAsync(cancellationToken).ConfigureAwait(false),
            Soft1Sources.StockMovements => await LoadStockMovementsAsync(query, source, cancellationToken).ConfigureAwait(false),
            Soft1Sources.ItemSupplierPrices => await LoadItemSupplierPricesAsync(cancellationToken).ConfigureAwait(false),
            Soft1Sources.ProjectTransactions => await LoadProjectTransactionsAsync(query, source, cancellationToken).ConfigureAwait(false),
            Soft1Sources.ProjectFinancials => await LoadProjectFinancialsAsync(cancellationToken).ConfigureAwait(false),

            Soft1Sources.SalesByCustomer => await LoadSalesByCustomerAsync(query, source, cancellationToken).ConfigureAwait(false),
            Soft1Sources.SalesByItem => await LoadSalesByItemAsync(query, source, cancellationToken).ConfigureAwait(false),
            Soft1Sources.SalesBySalesperson => await LoadSalesBySalespersonAsync(query, source, cancellationToken).ConfigureAwait(false),
            Soft1Sources.CustomerCreditStatus => await LoadCreditStatusAsync(cancellationToken).ConfigureAwait(false),

            _ => throw new ArgumentException($"Unknown Soft1 data source '{source.Id}'.")
        };

        return [.. rows.Select(row => Soft1Sources.Project(source, row))];
    }

    // ---- Reference data -------------------------------------------------------------------------------------

    private async Task<IReadOnlyList<Row>> LoadTradersAsync(string readId, bool customers, CancellationToken cancellationToken)
    {
        var rows = await reader.ReadAsync(readId, Soft1ReadScope.Everything, cancellationToken).ConfigureAwait(false);

        foreach (var row in rows)
        {
            row["status"] = ActiveStatus(row);
        }

        return rows;
    }

    private async Task<IReadOnlyList<Row>> LoadItemsAsync(CancellationToken cancellationToken)
    {
        var rows = await reader.ReadAsync(ItemsRead, Soft1ReadScope.Everything, cancellationToken).ConfigureAwait(false);

        foreach (var row in rows)
        {
            row["status"] = ActiveStatus(row);
        }

        return rows;
    }

    private Task<IReadOnlyList<Row>> LoadProjectsAsync(CancellationToken cancellationToken) =>
        reader.ReadAsync(ProjectsRead, Soft1ReadScope.Everything, cancellationToken);

    private async Task<IReadOnlyList<Row>> LoadCompaniesAsync(CancellationToken cancellationToken)
    {
        var rows = await reader.ReadAsync(CompaniesRead, Soft1ReadScope.Everything, cancellationToken).ConfigureAwait(false);
        var currencies = await CurrencyCodesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var row in rows)
        {
            row["company_code"] = row["company_id"];
            row["base_currency"] = Code(currencies, row["currency_id"]);
            row["country"] = row["country_id"];
        }

        return rows;
    }

    private async Task<IReadOnlyList<Row>> LoadPaymentTermsAsync(CancellationToken cancellationToken)
    {
        var rows = await reader.ReadAsync(PaymentTermsRead, Soft1ReadScope.Everything, cancellationToken).ConfigureAwait(false);

        foreach (var row in rows)
        {
            row["type"] = Entity(row["entity"]) switch
            {
                CustomerEntity => "sales",
                SupplierEntity => "purchases",
                _ => null
            };
        }

        return rows;
    }

    private async Task<IReadOnlyList<Row>> LoadTaxCodesAsync(CancellationToken cancellationToken)
    {
        var rows = await reader.ReadAsync(TaxCodesRead, Soft1ReadScope.Everything, cancellationToken).ConfigureAwait(false);

        foreach (var row in rows)
        {
            row["code"] = row["tax_code_id"];
        }

        return rows;
    }

    private async Task<IReadOnlyList<Row>> LoadCategoriesAsync(string readId, int entity, CancellationToken cancellationToken)
    {
        var rows = await reader.ReadAsync(readId, Soft1ReadScope.Everything, cancellationToken).ConfigureAwait(false);
        return [.. rows.Where(row => Entity(row["entity"]) == entity)];
    }

    private async Task<IReadOnlyList<Row>> LoadDocumentTypesAsync(CancellationToken cancellationToken)
    {
        var rows = await reader.ReadAsync(DocumentTypesRead, Soft1ReadScope.Everything, cancellationToken).ConfigureAwait(false);

        foreach (var row in rows)
        {
            var module = Entity(row["module"]);
            row["code"] = row["document_type_id"];
            row["category"] = ModuleName(module);
            row["direction"] = module switch
            {
                1351 or 1381 or 1361 => "out",
                1251 or 1281 or 1261 => "in",
                _ => null
            };
        }

        return rows;
    }

    private async Task<IReadOnlyList<Row>> LoadSalespeopleAsync(CancellationToken cancellationToken)
    {
        var personsTask = reader.ReadAsync(PersonsRead, Soft1ReadScope.Everything, cancellationToken);
        var rolesTask = reader.ReadAsync(PersonRolesRead, Soft1ReadScope.Everything, cancellationToken);
        await Task.WhenAll(personsTask, rolesTask).ConfigureAwait(false);

        // Entity 30 is "salesperson" (31 collector, 32 buyer, 33 technician) in PRSNIN.
        var salespeople = rolesTask.Result
            .Where(static role => Entity(role["entity"]) == SalespersonEntity)
            .Select(static role => Text(role["person_id"]))
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return
        [
            .. personsTask.Result
                .Where(person => Text(person["person_id"]) is { } id && salespeople.Contains(id))
                .Select(static person =>
                {
                    person["salesperson_id"] = person["person_id"];
                    person["team"] = person["department"];
                    return person;
                })
        ];
    }

    private async Task<IReadOnlyList<Row>> LoadAccountsAsync(CancellationToken cancellationToken)
    {
        var rows = await reader.ReadAsync(AccountsRead, Soft1ReadScope.Everything, cancellationToken).ConfigureAwait(false);
        var currencies = await CurrencyCodesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var row in rows)
        {
            row["currency"] = Code(currencies, row["currency_id"]);
        }

        return rows;
    }

    // ---- Shared lookups, kept for the length of one request -----------------------------------------------

    private Task<T> Memo<T>(string key, Func<Task<T>> load)
    {
        lock (memoGate)
        {
            if (memo.TryGetValue(key, out var existing))
            {
                return (Task<T>)existing;
            }

            var task = load();
            memo[key] = task;
            return task;
        }
    }

    /// <summary>Currency id → its short code (<c>SOCURRENCY.SHORTCUT</c>, e.g. EUR).</summary>
    private Task<Dictionary<string, string>> CurrencyCodesAsync(CancellationToken cancellationToken) =>
        Memo("currencies", async () =>
        {
            var rows = await reader.ReadAsync(CurrenciesRead, Soft1ReadScope.Everything, cancellationToken).ConfigureAwait(false);
            return rows
                .Where(static row => Text(row["currency_id"]) is not null)
                .GroupBy(static row => Text(row["currency_id"])!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    static group => group.Key,
                    static group => Text(group.First()["code"]) ?? Text(group.First()["name"]) ?? group.Key,
                    StringComparer.OrdinalIgnoreCase);
        });

    /// <summary>The signed-in company's currency code — the currency of every "local" amount.</summary>
    private Task<string?> CompanyCurrencyAsync(CancellationToken cancellationToken) =>
        Memo("company-currency", async () =>
        {
            var companies = await reader.ReadAsync(CompaniesRead, Soft1ReadScope.Everything, cancellationToken).ConfigureAwait(false);
            var currencies = await CurrencyCodesAsync(cancellationToken).ConfigureAwait(false);
            return companies.Count == 0 ? null : Code(currencies, companies[0]["currency_id"]);
        });

    /// <summary>(entity, TPRMS) → the transaction type's name and debit/credit flags.</summary>
    private Task<Dictionary<(int Entity, string Id), TransactionType>> TransactionTypesAsync(CancellationToken cancellationToken) =>
        Memo("transaction-types", async () =>
        {
            var rows = await reader.ReadAsync(TransactionTypesRead, Soft1ReadScope.Everything, cancellationToken).ConfigureAwait(false);
            var types = new Dictionary<(int, string), TransactionType>();

            foreach (var row in rows)
            {
                if (Entity(row["entity"]) is { } entity && Text(row["transaction_type_id"]) is { } id)
                {
                    types[(entity, id.ToUpperInvariant())] = new TransactionType(
                        Text(row["name"]), row["debit_flag"] as bool? ?? false, row["credit_flag"] as bool? ?? false);
                }
            }

            return types;
        });

    /// <summary>Trader id → which entity it is, its code and name. The ledgers name a trader without its kind.</summary>
    private Task<Dictionary<string, TraderInfo>> TradersAsync(CancellationToken cancellationToken) =>
        Memo("traders", async () =>
        {
            var rows = await reader.ReadAsync(TradersRead, Soft1ReadScope.Everything, cancellationToken).ConfigureAwait(false);
            var traders = new Dictionary<string, TraderInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                if (Text(row["trader_id"]) is { } id)
                {
                    traders[id] = new TraderInfo(id, Entity(row["entity"]), Text(row["code"]), Text(row["name"]));
                }
            }

            return traders;
        });

    // ---- Small helpers ---------------------------------------------------------------------------------------

    private const int SupplierEntity = 12;
    private const int CustomerEntity = 13;
    private const int CashAccountEntity = 14;
    private const int SalespersonEntity = 30;
    private const int ItemEntity = 51;

    private sealed record TransactionType(string? Name, bool Debit, bool Credit);

    private sealed record TraderInfo(string Id, int? Entity, string? Code, string? Name);

    private static string? ActiveStatus(Row row) => row.TryGetValue("active", out var active) && active is bool flag
        ? flag ? "active" : "inactive"
        : null;

    private static string? Text(object? value) => value switch
    {
        null => null,
        string text => string.IsNullOrWhiteSpace(text) ? null : text,
        decimal number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    private static decimal? Number(object? value) => value as decimal?;

    private static decimal Sum(object? value) => value as decimal? ?? 0m;

    private static int? Entity(object? value) => value switch
    {
        decimal number when number == decimal.Truncate(number) => (int)number,
        string text => Soft1Values.Integer(text),
        _ => null
    };

    private static string? Code(Dictionary<string, string> codes, object? id) =>
        Text(id) is { } key ? codes.GetValueOrDefault(key, key) : null;

    private static string? ModuleName(int? module) => module switch
    {
        1351 => "sales",
        1251 => "purchases",
        1151 => "inventory",
        1381 => "customer_cash",
        1281 => "supplier_cash",
        1481 or 1453 or 1414 => "cash",
        1089 => "general_ledger",
        1261 => "expenses",
        1361 => "revenue",
        1181 or 8100 => "cheques",
        _ => null
    };

    private static DateTime? DateValue(object? value) => value as DateTime?;
}
