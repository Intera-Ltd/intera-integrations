using Intera.Common.Models.Query;
using Intera.Soft1.Api.Models;
using System.Globalization;

namespace Intera.Soft1.Api.Services;

using static Soft1ReadCatalog;
using Row = Dictionary<string, object?>;

public sealed partial class Soft1DataService
{
    /// <summary>The trader ledger (<c>TRDTRN</c>) of the given fiscal years, read once per request per year set.</summary>
    private Task<IReadOnlyList<Row>> TraderTransactionsAsync(IReadOnlyList<int> years, CancellationToken cancellationToken) =>
        Memo($"trdtrn-{string.Join(',', years)}", () => reader.ReadAsync(
            TraderTransactionsRead, new Soft1ReadScope(FiscalYears: years), cancellationToken));

    /// <summary>
    /// A posting's debit and credit on the trader's account.
    /// </summary>
    /// <remarks>
    /// <c>TRDTRN</c> has no debit or credit column — only <c>TRNVAL</c>, whose side is decided by the transaction
    /// type: <c>TPRMS.FLG01</c> debits the account, <c>FLG02</c> credits it. That is the rule Soft1's own balance
    /// SQL applies (<c>TRNVAL*(FLG01-FLG02)</c>, seen in SoftOne-generated browser SQL); it is inferred, not
    /// documented, and is the first thing to check against a customer's statement on a live installation. A type
    /// the table does not know leaves both sides null rather than guessing one.
    /// </remarks>
    private static (decimal? Debit, decimal? Credit) SignedValue(
        Dictionary<(int Entity, string Id), TransactionType> types,
        int? entity,
        object? transactionType,
        object? value)
    {
        if (entity is null || Text(transactionType) is not { } id || value is not decimal amount
            || !types.TryGetValue((entity.Value, id.ToUpperInvariant()), out var type))
        {
            return (null, null);
        }

        return (type.Debit ? amount : 0m, type.Credit ? amount : 0m);
    }

    private async Task<IReadOnlyList<Row>> LoadTraderTransactionsAsync(
        int entity,
        QueryRequest? query,
        Soft1Source source,
        CancellationToken cancellationToken)
    {
        var (from, to) = Window(source, query);
        var years = FiscalYears(from, to);

        var transactionsTask = TraderTransactionsAsync(years, cancellationToken);
        var typesTask = TransactionTypesAsync(cancellationToken);
        var currenciesTask = CurrencyCodesAsync(cancellationToken);
        await Task.WhenAll(transactionsTask, typesTask, currenciesTask).ConfigureAwait(false);

        var trader = entity == CustomerEntity ? "customer_id" : "supplier_id";
        var rows = new List<Row>();

        foreach (var transaction in transactionsTask.Result.Where(row => Entity(row["entity"]) == entity))
        {
            var (debit, credit) = SignedValue(typesTask.Result, entity, transaction["transaction_type_id"], transaction["value"]);
            var (localDebit, localCredit) = SignedValue(typesTask.Result, entity, transaction["transaction_type_id"], transaction["value_local"]);
            var type = Text(transaction["transaction_type_id"]) is { } typeId
                ? typesTask.Result.GetValueOrDefault((entity, typeId.ToUpperInvariant()))
                : null;

            rows.Add(new Row(StringComparer.OrdinalIgnoreCase)
            {
                ["transaction_id"] = transaction["transaction_id"],
                [trader] = transaction["trader_id"],
                ["transaction_date"] = transaction["transaction_date"],
                ["transaction_type"] = type?.Name,
                ["transaction_type_id"] = transaction["transaction_type_id"],
                ["document_id"] = transaction["document_id"],
                ["document_number"] = transaction["document_number"],
                ["debit"] = debit,
                ["credit"] = credit,
                ["balance_effect"] = debit is null ? null : debit - credit,
                ["amount_local"] = localDebit is null ? null : localDebit - localCredit,
                ["currency"] = Code(currenciesTask.Result, transaction["currency_id"]),
                ["reference"] = transaction["reference"],
                ["project_id"] = transaction["project_id"],
                ["salesperson_id"] = transaction["salesperson_id"],
                ["branch_id"] = transaction["branch_id"]
            });
        }

        return rows;
    }

    /// <summary>
    /// Per trader, in company currency: <c>TRDFINDATA.LBAL</c> is the balance now, the current fiscal year's
    /// postings give debit and credit, and the opening balance is what the balance was before them.
    /// </summary>
    private async Task<IReadOnlyList<Row>> LoadBalancesAsync(int entity, CancellationToken cancellationToken)
    {
        IReadOnlyList<int> year = [DateTime.Today.Year];

        var tradersTask = TradersAsync(cancellationToken);
        var balancesTask = reader.ReadAsync(TraderBalancesRead, Soft1ReadScope.Everything, cancellationToken);
        var transactionsTask = TraderTransactionsAsync(year, cancellationToken);
        var typesTask = TransactionTypesAsync(cancellationToken);
        var openTask = OpenItemsAsync(cancellationToken);
        var currencyTask = CompanyCurrencyAsync(cancellationToken);
        var companyCurrencyIdTask = CompanyCurrencyIdAsync(cancellationToken);
        await Task.WhenAll(tradersTask, balancesTask, transactionsTask, typesTask, openTask, currencyTask, companyCurrencyIdTask)
            .ConfigureAwait(false);

        var balances = balancesTask.Result
            .Where(static row => Text(row["trader_id"]) is not null)
            .GroupBy(static row => Text(row["trader_id"])!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => Number(group.First()["balance_local"]), StringComparer.OrdinalIgnoreCase);

        var movements = new Dictionary<string, (decimal Debit, decimal Credit, DateTime? Last)>(StringComparer.OrdinalIgnoreCase);
        foreach (var transaction in transactionsTask.Result.Where(row => Entity(row["entity"]) == entity))
        {
            if (Text(transaction["trader_id"]) is not { } id)
            {
                continue;
            }

            var (debit, credit) = SignedValue(typesTask.Result, entity, transaction["transaction_type_id"], transaction["value_local"]);
            var current = movements.GetValueOrDefault(id);
            var date = DateValue(transaction["transaction_date"]);

            movements[id] = (current.Debit + (debit ?? 0m), current.Credit + (credit ?? 0m),
                current.Last is null || date > current.Last ? date ?? current.Last : current.Last);
        }

        // Overdue in company currency only: an open instalment carries its document's currency and no local
        // equivalent of the open amount, so a foreign one cannot be added to a local balance.
        var today = DateTime.Today;
        var overdue = openTask.Result
            .Where(item => Text(item["currency_id"]) == companyCurrencyIdTask.Result
                && DateValue(item["due_date"]) is { } due && due.Date < today)
            .GroupBy(static item => Text(item["trader_id"]) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Sum(static item => Sum(item["outstanding_amount"])), StringComparer.OrdinalIgnoreCase);

        var trader = entity == CustomerEntity ? "customer_id" : "supplier_id";

        return
        [
            .. tradersTask.Result.Values
                .Where(info => info.Entity == entity)
                .Select(info =>
                {
                    var outstanding = balances.GetValueOrDefault(info.Id);
                    var moved = movements.GetValueOrDefault(info.Id);

                    return new Row(StringComparer.OrdinalIgnoreCase)
                    {
                        [trader] = info.Id,
                        ["name"] = info.Name,
                        ["currency"] = currencyTask.Result,
                        ["opening_balance"] = outstanding is { } now ? now - (moved.Debit - moved.Credit) : null,
                        ["debit"] = moved.Debit,
                        ["credit"] = moved.Credit,
                        ["outstanding_balance"] = outstanding,
                        ["overdue_balance"] = overdue.GetValueOrDefault(info.Id),
                        ["last_transaction_date"] = moved.Last
                    };
                })
        ];
    }

    private Task<string?> CompanyCurrencyIdAsync(CancellationToken cancellationToken) =>
        Memo("company-currency-id", async () =>
        {
            var companies = await reader.ReadAsync(CompaniesRead, Soft1ReadScope.Everything, cancellationToken).ConfigureAwait(false);
            return companies.Count == 0 ? null : Text(companies[0]["currency_id"]);
        });

    /// <summary>Unsettled, uncancelled instalments of every trader (<c>FINPAYTERMS</c> with <c>ISCLOSE = 0</c>).</summary>
    private Task<IReadOnlyList<Row>> OpenItemsAsync(CancellationToken cancellationToken) =>
        Memo("open-items", async () =>
        {
            var rows = await reader.ReadAsync(OpenItemsRead, Soft1ReadScope.Everything, cancellationToken).ConfigureAwait(false);
            return (IReadOnlyList<Row>)[.. rows.Where(static row => row["cancelled"] as bool? != true)];
        });

    private async Task<IReadOnlyList<Row>> LoadOpenItemsAsync(int entity, CancellationToken cancellationToken)
    {
        var itemsTask = OpenItemsAsync(cancellationToken);
        var tradersTask = TradersAsync(cancellationToken);
        var currenciesTask = CurrencyCodesAsync(cancellationToken);
        await Task.WhenAll(itemsTask, tradersTask, currenciesTask).ConfigureAwait(false);

        var items = itemsTask.Result
            .Where(item => Text(item["trader_id"]) is { } id && tradersTask.Result.GetValueOrDefault(id)?.Entity == entity)
            .ToList();

        // An instalment names its document but not the document's number; the ledger has both. Read for the years
        // the open documents were issued in — the most recent Soft1:MaxFiscalYears of them, since older open items
        // are rare and every year is a whole-table read. An instalment outside those years keeps a null number.
        var years = items
            .Select(static item => DateValue(item["invoice_date"])?.Year)
            .OfType<int>()
            .Distinct()
            .OrderDescending()
            .Take(Math.Max(1, Options.MaxFiscalYears))
            .Order()
            .ToList();

        var documents = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
        if (years.Count > 0)
        {
            foreach (var transaction in await TraderTransactionsAsync(years, cancellationToken).ConfigureAwait(false))
            {
                if (Entity(transaction["entity"]) == entity && Text(transaction["document_id"]) is { } document)
                {
                    documents.TryAdd(document, transaction);
                }
            }
        }

        var trader = entity == CustomerEntity ? "customer_id" : "supplier_id";
        var today = DateTime.Today;

        return
        [
            .. items.Select(item =>
            {
                var document = Text(item["document_id"]) is { } id ? documents.GetValueOrDefault(id) : null;
                var original = Number(item["original_amount"]);
                var outstanding = Number(item["outstanding_amount"]);
                var due = DateValue(item["due_date"]);

                return new Row(StringComparer.OrdinalIgnoreCase)
                {
                    ["open_item_id"] = item["open_item_id"],
                    ["document_id"] = item["document_id"],
                    [trader] = item["trader_id"],
                    ["invoice_number"] = document?["document_number"],
                    ["invoice_date"] = item["invoice_date"],
                    ["due_date"] = due,
                    ["original_amount"] = original,
                    ["paid_amount"] = original is null || outstanding is null ? null : original - outstanding,
                    ["outstanding_amount"] = outstanding,
                    ["currency"] = Code(currenciesTask.Result, item["currency_id"]),
                    ["currency_id"] = item["currency_id"],
                    ["days_overdue"] = due is null ? null : (decimal)Math.Max(0, (today - due.Value.Date).Days),
                    ["salesperson_id"] = document?["salesperson_id"],
                    ["project_id"] = document?["project_id"]
                };
            })
        ];
    }

    private async Task<IReadOnlyList<Row>> LoadAgeingAsync(int entity, CancellationToken cancellationToken)
    {
        var itemsTask = LoadOpenItemsAsync(entity, cancellationToken);
        var tradersTask = TradersAsync(cancellationToken);
        await Task.WhenAll(itemsTask, tradersTask).ConfigureAwait(false);

        var trader = entity == CustomerEntity ? "customer_id" : "supplier_id";
        var today = DateTime.Today;

        return
        [
            .. itemsTask.Result
                .GroupBy(item => (Trader: Text(item[trader]) ?? string.Empty, Currency: Text(item["currency"]) ?? string.Empty))
                .Select(group =>
                {
                    decimal current = 0m, d30 = 0m, d60 = 0m, d90 = 0m, d120 = 0m, older = 0m;

                    foreach (var item in group)
                    {
                        var amount = Sum(item["outstanding_amount"]);
                        var days = DateValue(item["due_date"]) is { } due ? (today - due.Date).Days : 0;

                        switch (days)
                        {
                            case <= 0: current += amount; break;
                            case <= 30: d30 += amount; break;
                            case <= 60: d60 += amount; break;
                            case <= 90: d90 += amount; break;
                            case <= 120: d120 += amount; break;
                            default: older += amount; break;
                        }
                    }

                    return new Row(StringComparer.OrdinalIgnoreCase)
                    {
                        [trader] = group.Key.Trader.Length == 0 ? null : group.Key.Trader,
                        ["name"] = tradersTask.Result.GetValueOrDefault(group.Key.Trader)?.Name,
                        ["currency"] = group.Key.Currency.Length == 0 ? null : group.Key.Currency,
                        ["current_amount"] = current,
                        ["days_1_30"] = d30,
                        ["days_31_60"] = d60,
                        ["days_61_90"] = d90,
                        ["days_91_120"] = d120,
                        ["days_120_plus"] = older,
                        ["total_outstanding"] = current + d30 + d60 + d90 + d120 + older
                    };
                })
        ];
    }

    private async Task<IReadOnlyList<Row>> LoadLedgerAsync(QueryRequest? query, Soft1Source source, CancellationToken cancellationToken)
    {
        var (from, to) = Window(source, query);

        var linesTask = reader.ReadAsync(LedgerLinesRead, new Soft1ReadScope(FiscalYears: FiscalYears(from, to)), cancellationToken);
        var accountsTask = AccountsAsync(cancellationToken);
        var tradersTask = TradersAsync(cancellationToken);
        var currenciesTask = CurrencyCodesAsync(cancellationToken);
        await Task.WhenAll(linesTask, accountsTask, tradersTask, currenciesTask).ConfigureAwait(false);

        foreach (var line in linesTask.Result)
        {
            var trader = Text(line["trader_id"]) is { } id ? tradersTask.Result.GetValueOrDefault(id) : null;
            line["account_code"] = Text(line["account_id"]) is { } account ? accountsTask.Result.GetValueOrDefault(account)?["account_code"] : null;
            line["currency"] = Code(currenciesTask.Result, line["currency_id"]);
            line["customer_id"] = trader?.Entity == CustomerEntity ? trader.Id : null;
            line["supplier_id"] = trader?.Entity == SupplierEntity ? trader.Id : null;
            line["reference"] = line["document_number"];
        }

        return linesTask.Result;
    }

    private Task<Dictionary<string, Row>> AccountsAsync(CancellationToken cancellationToken) =>
        Memo("accounts", async () =>
        {
            var rows = await reader.ReadAsync(AccountsRead, Soft1ReadScope.Everything, cancellationToken).ConfigureAwait(false);
            return rows
                .Where(static row => Text(row["account_id"]) is not null)
                .GroupBy(static row => Text(row["account_id"])!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        });

    /// <summary>
    /// Per account and fiscal period, from <c>ACNBALSHEET</c>'s company-currency debit and credit. The opening
    /// balance of a period is the sum of the year's earlier periods, so a year opens at zero unless its period 0
    /// (the opening entries) carries the carried-forward balance — which is how Soft1 books it.
    /// </summary>
    private async Task<IReadOnlyList<Row>> LoadAccountBalancesAsync(QueryRequest? query, Soft1Source source, CancellationToken cancellationToken)
    {
        var (from, to) = Window(source, query);

        var balancesTask = reader.ReadAsync(LedgerBalancesRead, new Soft1ReadScope(FiscalYears: FiscalYears(from, to)), cancellationToken);
        var accountsTask = AccountsAsync(cancellationToken);
        var currencyTask = CompanyCurrencyAsync(cancellationToken);
        await Task.WhenAll(balancesTask, accountsTask, currencyTask).ConfigureAwait(false);

        var rows = new List<Row>();

        foreach (var group in balancesTask.Result.GroupBy(static row => (Account: Text(row["account_id"]) ?? string.Empty, Year: Entity(row["fiscal_year"]) ?? 0)))
        {
            var running = 0m;
            var account = accountsTask.Result.GetValueOrDefault(group.Key.Account);

            foreach (var period in group.OrderBy(static row => Entity(row["period_number"]) ?? 0))
            {
                var number = Entity(period["period_number"]) ?? 0;
                var debit = Sum(period["debit"]);
                var credit = Sum(period["credit"]);

                rows.Add(new Row(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_id"] = group.Key.Account,
                    ["account_code"] = account?["account_code"],
                    ["account_name"] = account?["account_name"],
                    ["period"] = string.Create(CultureInfo.InvariantCulture, $"{group.Key.Year}-{number:D2}"),
                    ["fiscal_year"] = (decimal)group.Key.Year,
                    ["period_number"] = (decimal)number,
                    ["period_start"] = group.Key.Year > 0
                        ? number > 12 ? new DateTime(group.Key.Year, 12, 31) : new DateTime(group.Key.Year, Math.Max(1, number), 1)
                        : null,
                    ["currency"] = currencyTask.Result,
                    ["opening_balance"] = running,
                    ["debit"] = debit,
                    ["credit"] = credit,
                    ["closing_balance"] = running + debit - credit
                });

                running += debit - credit;
            }
        }

        return rows;
    }

    private async Task<IReadOnlyList<Row>> LoadCashAccountsAsync(CancellationToken cancellationToken)
    {
        var accountsTask = reader.ReadAsync(CashAccountsRead, Soft1ReadScope.Everything, cancellationToken);
        var balancesTask = reader.ReadAsync(TraderBalancesRead, Soft1ReadScope.Everything, cancellationToken);
        var bankAccountsTask = reader.ReadAsync(TraderBankAccountsRead, Soft1ReadScope.Everything, cancellationToken);
        var banksTask = reader.ReadAsync(BanksRead, Soft1ReadScope.Everything, cancellationToken);
        var currenciesTask = CurrencyCodesAsync(cancellationToken);
        await Task.WhenAll(accountsTask, balancesTask, bankAccountsTask, banksTask, currenciesTask).ConfigureAwait(false);

        var balances = balancesTask.Result
            .Where(static row => Text(row["trader_id"]) is not null)
            .GroupBy(static row => Text(row["trader_id"])!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        var bankAccounts = bankAccountsTask.Result
            .Where(static row => Text(row["trader_id"]) is not null)
            .GroupBy(static row => Text(row["trader_id"])!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.OrderBy(static row => Sum(row["line_number"])).First(), StringComparer.OrdinalIgnoreCase);

        var banks = banksTask.Result
            .Where(static row => Text(row["bank_id"]) is not null)
            .GroupBy(static row => Text(row["bank_id"])!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => Text(group.First()["name"]), StringComparer.OrdinalIgnoreCase);

        foreach (var account in accountsTask.Result)
        {
            var id = Text(account["account_id"]) ?? string.Empty;
            var balance = balances.GetValueOrDefault(id);
            var bank = bankAccounts.GetValueOrDefault(id);

            account["bank_name"] = Text(bank?["bank_id"]) is { } bankId ? banks.GetValueOrDefault(bankId) : null;
            account["iban"] = bank?["iban"] ?? bank?["account_number"];
            account["account_type"] = bank is null ? "cash" : "bank";
            account["currency"] = Code(currenciesTask.Result, account["currency_id"]);
            account["current_balance"] = Number(balance?["balance"]) ?? Number(balance?["balance_local"]);
        }

        return accountsTask.Result;
    }

    private async Task<IReadOnlyList<Row>> LoadCashTransactionsAsync(QueryRequest? query, Soft1Source source, CancellationToken cancellationToken)
    {
        var (from, to) = Window(source, query);

        var transactionsTask = TraderTransactionsAsync(FiscalYears(from, to), cancellationToken);
        var typesTask = TransactionTypesAsync(cancellationToken);
        var tradersTask = TradersAsync(cancellationToken);
        var currenciesTask = CurrencyCodesAsync(cancellationToken);
        await Task.WhenAll(transactionsTask, typesTask, tradersTask, currenciesTask).ConfigureAwait(false);

        // The customer or supplier of a cash posting is the other trader posted by the same document.
        var counterparts = transactionsTask.Result
            .Where(static row => Entity(row["entity"]) is CustomerEntity or SupplierEntity && Text(row["document_id"]) is not null)
            .GroupBy(static row => Text(row["document_id"])!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        var rows = new List<Row>();

        foreach (var transaction in transactionsTask.Result.Where(static row => Entity(row["entity"]) == CashAccountEntity))
        {
            var (debit, credit) = SignedValue(typesTask.Result, CashAccountEntity, transaction["transaction_type_id"], transaction["value"]);
            var (localDebit, localCredit) = SignedValue(typesTask.Result, CashAccountEntity, transaction["transaction_type_id"], transaction["value_local"]);
            var counterpart = Text(transaction["document_id"]) is { } document ? counterparts.GetValueOrDefault(document) : null;
            var counterpartEntity = Entity(counterpart?["entity"]);
            var counterpartTrader = Text(counterpart?["trader_id"]) is { } traderId ? tradersTask.Result.GetValueOrDefault(traderId) : null;
            var type = Text(transaction["transaction_type_id"]) is { } typeId
                ? typesTask.Result.GetValueOrDefault((CashAccountEntity, typeId.ToUpperInvariant()))
                : null;

            rows.Add(new Row(StringComparer.OrdinalIgnoreCase)
            {
                ["transaction_id"] = transaction["transaction_id"],
                ["bank_account_id"] = transaction["trader_id"],
                ["transaction_date"] = transaction["transaction_date"],
                ["amount"] = debit is null ? null : debit - credit,
                ["amount_local"] = localDebit is null ? null : localDebit - localCredit,
                ["currency"] = Code(currenciesTask.Result, transaction["currency_id"]),
                ["transaction_type"] = type?.Name,
                ["counterparty"] = counterpartTrader?.Name,
                ["reference"] = transaction["reference"],
                ["document_number"] = transaction["document_number"],
                ["customer_id"] = counterpartEntity == CustomerEntity ? counterpart?["trader_id"] : null,
                ["supplier_id"] = counterpartEntity == SupplierEntity ? counterpart?["trader_id"] : null,
                ["matched_document_id"] = transaction["document_id"]
            });
        }

        return rows;
    }

    private Task<Dictionary<string, Row>> ItemsByIdAsync(CancellationToken cancellationToken) =>
        Memo("items", async () =>
        {
            var rows = await reader.ReadAsync(ItemsRead, Soft1ReadScope.Everything, cancellationToken).ConfigureAwait(false);
            return rows
                .Where(static row => Text(row["item_id"]) is not null)
                .GroupBy(static row => Text(row["item_id"])!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        });

    /// <summary>
    /// Per item and warehouse, the current fiscal year's running quantity and value (<c>MTRFINDATA</c>). Reserved
    /// and on-order quantities are not in that table — they live on open order lines, one call per order — so
    /// they read null, as does the available quantity that would depend on them.
    /// </summary>
    private async Task<IReadOnlyList<Row>> LoadStockBalancesAsync(CancellationToken cancellationToken)
    {
        var balancesTask = reader.ReadAsync(ItemWarehouseBalancesRead, new Soft1ReadScope(FiscalYears: [DateTime.Today.Year]), cancellationToken);
        var itemsTask = ItemsByIdAsync(cancellationToken);
        await Task.WhenAll(balancesTask, itemsTask).ConfigureAwait(false);

        foreach (var row in balancesTask.Result)
        {
            var item = Text(row["item_id"]) is { } id ? itemsTask.Result.GetValueOrDefault(id) : null;
            var quantity = Number(row["quantity"]);
            var value = Number(row["value_in"]) is { } valueIn ? valueIn - Sum(row["value_out"]) : (decimal?)null;

            row["item_code"] = item?["item_code"];
            row["description"] = item?["description"];
            row["unit"] = item?["unit"];
            row["quantity_on_hand"] = quantity;
            row["stock_value"] = value;
            row["average_cost"] = quantity is { } qty && qty != 0 && value is { } total ? Math.Round(total / qty, 6) : null;
        }

        return balancesTask.Result;
    }

    private async Task<IReadOnlyList<Row>> LoadStockMovementsAsync(QueryRequest? query, Soft1Source source, CancellationToken cancellationToken)
    {
        var (from, to) = Window(source, query);

        var movementsTask = reader.ReadAsync(ItemMovementsRead, new Soft1ReadScope(FiscalYears: FiscalYears(from, to)), cancellationToken);
        var typesTask = TransactionTypesAsync(cancellationToken);
        var tradersTask = TradersAsync(cancellationToken);
        await Task.WhenAll(movementsTask, typesTask, tradersTask).ConfigureAwait(false);

        foreach (var row in movementsTask.Result)
        {
            var trader = Text(row["trader_id"]) is { } id ? tradersTask.Result.GetValueOrDefault(id) : null;
            var type = Text(row["movement_type_id"]) is { } typeId ? typesTask.Result.GetValueOrDefault((ItemEntity, typeId.ToUpperInvariant())) : null;

            row["movement_type"] = type?.Name;
            row["unit_cost"] = row["unit_price"];
            row["total_cost"] = row["cost"];
            row["module"] = ModuleName(Entity(row["module"]));
            row["customer_id"] = trader?.Entity == CustomerEntity ? trader.Id : null;
            row["supplier_id"] = trader?.Entity == SupplierEntity ? trader.Id : null;
        }

        return movementsTask.Result;
    }

    /// <summary>
    /// A supplier's code for an item (<c>MTRSUPCODE</c>) with the item's last purchase price and date from
    /// <c>MTRDATA</c>. That price is the item's, from whichever supplier sold it last — Soft1 keeps no price per
    /// supplier there — and it is in company currency. Agreement prices and validity have no standard table.
    /// </summary>
    private async Task<IReadOnlyList<Row>> LoadItemSupplierPricesAsync(CancellationToken cancellationToken)
    {
        var codesTask = reader.ReadAsync(ItemSupplierCodesRead, Soft1ReadScope.Everything, cancellationToken);
        var yearTask = reader.ReadAsync(ItemYearDataRead, new Soft1ReadScope(FiscalYears: [DateTime.Today.Year]), cancellationToken);
        var itemsTask = ItemsByIdAsync(cancellationToken);
        var currencyTask = CompanyCurrencyAsync(cancellationToken);
        await Task.WhenAll(codesTask, yearTask, itemsTask, currencyTask).ConfigureAwait(false);

        var purchases = yearTask.Result
            .Where(static row => Text(row["item_id"]) is not null)
            .GroupBy(static row => Text(row["item_id"])!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var row in codesTask.Result)
        {
            var id = Text(row["item_id"]) ?? string.Empty;
            var purchase = purchases.GetValueOrDefault(id);

            row["item_code"] = itemsTask.Result.GetValueOrDefault(id)?["item_code"];
            row["last_purchase_price"] = purchase?["last_purchase_price"];
            row["last_purchase_date"] = purchase?["last_purchase_date"];
            row["currency"] = currencyTask.Result;
        }

        return codesTask.Result;
    }

    private async Task<IReadOnlyList<Row>> LoadProjectTransactionsAsync(QueryRequest? query, Soft1Source source, CancellationToken cancellationToken)
    {
        var (from, to) = Window(source, query);

        var transactionsTask = TraderTransactionsAsync(FiscalYears(from, to), cancellationToken);
        var typesTask = TransactionTypesAsync(cancellationToken);
        var currenciesTask = CurrencyCodesAsync(cancellationToken);
        await Task.WhenAll(transactionsTask, typesTask, currenciesTask).ConfigureAwait(false);

        var rows = new List<Row>();

        foreach (var transaction in transactionsTask.Result)
        {
            var entity = Entity(transaction["entity"]);
            if (Text(transaction["project_id"]) is null || entity is not (CustomerEntity or SupplierEntity))
            {
                continue;
            }

            var (debit, credit) = SignedValue(typesTask.Result, entity, transaction["transaction_type_id"], transaction["value"]);
            var (localDebit, localCredit) = SignedValue(typesTask.Result, entity, transaction["transaction_type_id"], transaction["value_local"]);
            var type = Text(transaction["transaction_type_id"]) is { } typeId
                ? typesTask.Result.GetValueOrDefault((entity.Value, typeId.ToUpperInvariant()))
                : null;

            rows.Add(new Row(StringComparer.OrdinalIgnoreCase)
            {
                ["transaction_id"] = transaction["transaction_id"],
                ["project_id"] = transaction["project_id"],
                ["transaction_date"] = transaction["transaction_date"],
                ["transaction_type"] = type?.Name,
                ["side"] = entity == CustomerEntity ? "customer" : "supplier",
                ["document_id"] = transaction["document_id"],
                ["document_number"] = transaction["document_number"],
                ["customer_id"] = entity == CustomerEntity ? transaction["trader_id"] : null,
                ["supplier_id"] = entity == SupplierEntity ? transaction["trader_id"] : null,
                ["amount"] = debit is null ? null : debit - credit,
                ["amount_local"] = localDebit is null ? null : localDebit - localCredit,
                ["currency"] = Code(currenciesTask.Result, transaction["currency_id"]),
                ["description"] = transaction["reference"]
            });
        }

        return rows;
    }

    /// <summary>
    /// Per project, in company currency, over the <c>Soft1:DefaultFiscalYears</c> most recent years: a customer
    /// debit is revenue invoiced and a customer credit is money received; a supplier credit is cost invoiced and a
    /// supplier debit is money paid. Committed cost (open purchase orders) is not read and stays null.
    /// </summary>
    private async Task<IReadOnlyList<Row>> LoadProjectFinancialsAsync(CancellationToken cancellationToken)
    {
        var transactionsTask = TraderTransactionsAsync(FiscalYears(null, null), cancellationToken);
        var typesTask = TransactionTypesAsync(cancellationToken);
        var projectsTask = LoadProjectsAsync(cancellationToken);
        var currencyTask = CompanyCurrencyAsync(cancellationToken);
        await Task.WhenAll(transactionsTask, typesTask, projectsTask, currencyTask).ConfigureAwait(false);

        var totals = new Dictionary<string, decimal[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var transaction in transactionsTask.Result)
        {
            var entity = Entity(transaction["entity"]);
            if (Text(transaction["project_id"]) is not { } project || entity is not (CustomerEntity or SupplierEntity))
            {
                continue;
            }

            var (debit, credit) = SignedValue(typesTask.Result, entity, transaction["transaction_type_id"], transaction["value_local"]);
            if (!totals.TryGetValue(project, out var sums))
            {
                sums = new decimal[4];
                totals[project] = sums;
            }

            if (entity == CustomerEntity)
            {
                sums[0] += debit ?? 0m;
                sums[1] += credit ?? 0m;
            }
            else
            {
                sums[2] += credit ?? 0m;
                sums[3] += debit ?? 0m;
            }
        }

        var names = projectsTask.Result
            .Where(static row => Text(row["project_id"]) is not null)
            .GroupBy(static row => Text(row["project_id"])!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => Text(group.First()["name"]), StringComparer.OrdinalIgnoreCase);

        return
        [
            .. names.Keys.Union(totals.Keys, StringComparer.OrdinalIgnoreCase).Select(project =>
            {
                var sums = totals.GetValueOrDefault(project) ?? new decimal[4];

                return new Row(StringComparer.OrdinalIgnoreCase)
                {
                    ["project_id"] = project,
                    ["name"] = names.GetValueOrDefault(project),
                    ["revenue"] = sums[0],
                    ["invoiced_revenue"] = sums[0],
                    ["received_revenue"] = sums[1],
                    ["actual_cost"] = sums[2],
                    ["paid_cost"] = sums[3],
                    ["gross_margin"] = sums[0] - sums[2],
                    ["currency"] = currencyTask.Result
                };
            })
        ];
    }
}
