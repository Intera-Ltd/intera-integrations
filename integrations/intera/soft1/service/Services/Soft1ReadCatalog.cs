using Intera.Soft1.Api.Options;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;

namespace Intera.Soft1.Api.Services;

[JsonConverter(typeof(JsonStringEnumConverter<Soft1ReadMode>))]
public enum Soft1ReadMode
{
    /// <summary><c>getBrowserInfo</c>/<c>getBrowserData</c> over a business object's browser.</summary>
    Browser,

    /// <summary><c>selectorFields</c> over one database table: exactly the named columns, no paging.</summary>
    Table
}

[JsonConverter(typeof(JsonStringEnumConverter<Soft1ValueKind>))]
public enum Soft1ValueKind
{
    Text,
    Number,
    Date,
    Flag,

    /// <summary>The id half of a selector value — <c>"100|Cash"</c> reads <c>100</c>; a bare value reads whole.</summary>
    Id,

    /// <summary>The caption half of a selector value — <c>"100|Cash"</c> reads <c>Cash</c>; a bare value reads null.</summary>
    Label,

    /// <summary>The record id out of the browser's <c>ZOOMINFO</c> (<c>"CUSTOMER;47"</c> → <c>47</c>).</summary>
    RecordKey
}

/// <param name="Field">Row key the value is written to.</param>
/// <param name="Candidates">
/// Soft1 columns to read it from, first one present wins. In a browser these are the browser's column names
/// (<c>CUSTOMER.CODE</c>, <c>SALDOC.TRDR_CUSTOMER_NAME</c>); a leading <c>=</c> takes that column's value whole,
/// whatever <paramref name="Kind"/> says. In a table they are the table's own column names, and only the first
/// is used — <c>selectorFields</c> fails outright on a column the table lacks.
/// </param>
public sealed record Soft1Column(string Field, Soft1ValueKind Kind, IReadOnlyList<string> Candidates);

/// <summary>How one set of rows is read from Soft1. Every property can be overridden per installation.</summary>
/// <param name="Id">The key under <c>Soft1:Reads</c> that overrides it.</param>
/// <param name="Object">Browser: the business object. Table: the table name.</param>
/// <param name="List">Browser: the saved browser to run, empty for the default one.</param>
/// <param name="Filters">Browser: fixed <c>FILTERS</c>, <c>&amp;</c>-separated.</param>
/// <param name="DateFilter">
/// Browser: the dialog filter the reporting window is sent to, as <c>NAME=from&amp;NAME_TO=to</c>. Null reads
/// without one.
/// </param>
/// <param name="SeriesFilter">Browser: the dialog filter that takes a list of document series.</param>
/// <param name="KeyName">Table: the column <c>selectorFields</c> filters on.</param>
/// <param name="KeyValue">
/// Table: its value; <c>{company}</c> is the signed-in company and <c>{fiscal_year}</c> reads once per fiscal year
/// the query needs.
/// </param>
/// <param name="CompanyColumn">Table: rows of another company are dropped when this column says so.</param>
public sealed record Soft1Read(
    string Id,
    Soft1ReadMode Mode,
    string Object,
    IReadOnlyList<Soft1Column> Columns,
    string List = "",
    string Filters = "",
    string? DateFilter = null,
    string? SeriesFilter = null,
    string KeyName = "",
    string KeyValue = "",
    string? CompanyColumn = null)
{
    public bool PerFiscalYear => KeyValue.Contains("{fiscal_year}", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The built-in reads, and the installation's overrides applied to them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Written without a Soft1 installation to try it on.</b> The browser column names follow SoftOne's own
/// published examples (the Prosvasis GO API specification and the Power Automate connector: <c>CUSTOMER.AFM</c>,
/// <c>SALDOC.TRDR_CUSTOMER_NAME</c>, <c>CUSTOMER.TRDR_CUSFINDATA_LBAL</c>); the table and column names follow a
/// published dump of a real Soft1 database. What a given installation's default browser actually shows is decided
/// by that installation, and a column its browser lacks reads null — <c>GET /api/Schema/reads/{id}/probe</c> lists
/// which candidates matched. The fix is configuration, not code: <c>Soft1:Reads:&lt;id&gt;</c> can name a saved
/// browser (<c>List</c>) that has the columns, switch the read to a table (<c>Mode</c>), or map a field to other
/// columns (<c>Columns</c>).
/// </para>
/// <para>
/// <b>Tables are read through <c>selectorFields</c></b>, which BlackBook describes as returning "the data of a
/// table for the fields specified", filtered by one key column. It has no paging, so a per-year table
/// (<c>TRDTRN</c>, <c>ACNTRN</c>, <c>MTRTRN</c>) is read one fiscal year per call.
/// </para>
/// </remarks>
public sealed class Soft1ReadCatalog(IOptions<Soft1Options> options)
{
    public const string CustomersRead = "customers";
    public const string SuppliersRead = "suppliers";
    public const string ItemsRead = "items";
    public const string ProjectsRead = "projects";
    public const string SalesHeadersRead = "sales_documents";
    public const string PurchaseHeadersRead = "purchase_documents";
    public const string SeriesRead = "series";
    public const string DocumentTypesRead = "document_types";
    public const string TransactionTypesRead = "transaction_types";
    public const string WarehousesRead = "warehouses";
    public const string BranchesRead = "branches";
    public const string CompaniesRead = "companies";
    public const string CurrenciesRead = "currencies";
    public const string PaymentTermsRead = "payment_terms";
    public const string TaxCodesRead = "tax_codes";
    public const string UnitsRead = "units";
    public const string ItemCategoriesRead = "item_categories";
    public const string TraderCategoriesRead = "trader_categories";
    public const string AccountsRead = "accounts";
    public const string PersonsRead = "persons";
    public const string PersonRolesRead = "person_roles";
    public const string TraderBalancesRead = "trader_balances";
    public const string TradersRead = "traders";
    public const string CashAccountsRead = "cash_accounts";
    public const string TraderBankAccountsRead = "trader_bank_accounts";
    public const string BanksRead = "banks";
    public const string TraderTransactionsRead = "trader_transactions";
    public const string LedgerLinesRead = "ledger_lines";
    public const string LedgerBalancesRead = "ledger_balances";
    public const string ItemMovementsRead = "item_movements";
    public const string ItemWarehouseBalancesRead = "item_warehouse_balances";
    public const string ItemYearDataRead = "item_year_data";
    public const string ItemSupplierCodesRead = "item_supplier_codes";
    public const string OpenItemsRead = "open_items";

    private static Soft1Column Key(string field) => new(field, Soft1ValueKind.RecordKey, []);

    private static Soft1Column Col(string field, Soft1ValueKind kind, params string[] candidates) => new(field, kind, candidates);

    private static Soft1Column Text(string field, params string[] candidates) => new(field, Soft1ValueKind.Text, candidates);

    private static Soft1Column Num(string field, params string[] candidates) => new(field, Soft1ValueKind.Number, candidates);

    private static Soft1Column Date(string field, params string[] candidates) => new(field, Soft1ValueKind.Date, candidates);

    private static Soft1Column Flag(string field, params string[] candidates) => new(field, Soft1ValueKind.Flag, candidates);

    private static Soft1Column Id(string field, params string[] candidates) => new(field, Soft1ValueKind.Id, candidates);

    private static Soft1Column Label(string field, params string[] candidates) => new(field, Soft1ValueKind.Label, candidates);

    private static Soft1Read Trader(string id, string obj, string balanceObject, bool customers) => new(
        id,
        Soft1ReadMode.Browser,
        obj,
        [
            Key(customers ? "customer_id" : "supplier_id"),
            Text(customers ? "customer_code" : "supplier_code", $"{obj}.CODE"),
            Text("name", $"{obj}.NAME"),
            Text("vat_number", $"{obj}.AFM"),
            Flag("active", $"{obj}.ISACTIVE"),
            Id("category_id", $"{obj}.TRDCATEGORY"),
            Label("category", $"{obj}.TRDCATEGORY"),
            Id("salesperson_id", $"{obj}.SALESMAN"),
            Label("salesperson_name", $"{obj}.SALESMAN"),
            Label("currency", $"{obj}.SOCURRENCY"),
            Label("payment_terms", $"{obj}.PAYMENT"),
            Id("payment_terms_id", $"{obj}.PAYMENT"),
            Num("credit_limit", $"{obj}.CRDLIMIT1"),
            Num("balance", $"{obj}.TRDR_{balanceObject}_LBAL", $"{obj}.TRDR_TRDFINDATA_LBAL", $"{obj}.TRDR_CUSFINDATA_LBAL"),
            Label("country", $"{obj}.COUNTRY"),
            Text("city", $"{obj}.CITY"),
            Text("address", $"{obj}.ADDRESS"),
            Text("email", $"{obj}.EMAIL"),
            Text("phone", $"{obj}.PHONE01", $"{obj}.PHONE02"),
            Date("created_at", $"{obj}.INSDATE"),
            Date("updated_at", $"{obj}.UPDDATE")
        ]);

    /// <summary>
    /// A document browser. The date goes to <c>FINDOC.TRNDATE</c> and the series to <c>&lt;obj&gt;.SERIES</c> — the
    /// filter names SoftOne's own GO API uses for SALDOC and PURDOC.
    /// </summary>
    private static Soft1Read Documents(string id, string obj, string trader, bool customers) => new(
        id,
        Soft1ReadMode.Browser,
        obj,
        [
            Key("document_id"),
            Text("document_number", $"{obj}.FINCODE"),
            Date("document_date", $"{obj}.TRNDATE"),
            Id("series_id", $"{obj}.SERIES"),
            Id(customers ? "customer_id" : "supplier_id", $"{obj}.TRDR"),
            Text(customers ? "customer_code" : "supplier_code", $"{obj}.TRDR_{trader}_CODE"),
            Col(customers ? "customer_name" : "supplier_name", Soft1ValueKind.Label, $"{obj}.TRDR", $"={obj}.TRDR_{trader}_NAME"),
            Flag("cancelled", $"{obj}.ISCANCEL"),
            Id("fully_transformed", $"{obj}.FULLYTRANSF"),
            Id("salesperson_id", $"{obj}.SALESMAN"),
            Label("currency", $"{obj}.SOCURRENCY"),
            Num("net_amount", $"{obj}.NETAMNT"),
            Num("vat_amount", $"{obj}.VATAMNT"),
            Num("total_amount", $"{obj}.SUMAMNT"),
            Id("project_id", $"{obj}.PRJC"),
            Text("reference", $"{obj}.COMMENTS"),
            Label("payment_terms", $"{obj}.PAYMENT"),
            Date("created_at", $"{obj}.INSDATE"),
            Date("updated_at", $"{obj}.UPDDATE")
        ],
        DateFilter: "FINDOC.TRNDATE",
        SeriesFilter: $"{obj}.SERIES");

    private static Soft1Read Table(string id, string table, string keyName, string keyValue, string? companyColumn, params Soft1Column[] columns) =>
        new(id, Soft1ReadMode.Table, table, columns, KeyName: keyName, KeyValue: keyValue, CompanyColumn: companyColumn);

    private static readonly IReadOnlyList<Soft1Read> BuiltIn =
    [
        Trader(CustomersRead, "CUSTOMER", "CUSFINDATA", customers: true),
        Trader(SuppliersRead, "SUPPLIER", "SUPFINDATA", customers: false),

        new(ItemsRead, Soft1ReadMode.Browser, "ITEM",
        [
            Key("item_id"),
            Text("item_code", "ITEM.CODE"),
            Text("description", "ITEM.NAME"),
            Id("category_id", "ITEM.MTRCATEGORY"),
            Label("category", "ITEM.MTRCATEGORY"),
            Id("group_id", "ITEM.MTRGROUP"),
            Label("group", "ITEM.MTRGROUP"),
            Id("brand_id", "ITEM.MTRMARK"),
            Label("brand", "ITEM.MTRMARK"),
            Id("unit_id", "ITEM.MTRUNIT1"),
            Label("unit", "ITEM.MTRUNIT1"),
            Id("vat_id", "ITEM.VAT"),
            Label("vat_code", "ITEM.VAT"),
            Flag("active", "ITEM.ISACTIVE"),
            Num("sales_price", "ITEM.PRICEW"),
            Num("retail_price", "ITEM.PRICER"),
            Num("cost_price", "ITEM.MTRL_ITEMTRDATA_PURLPRICE", "ITEM.MTRL_MTRDATA_PURLPRICE"),
            Label("currency", "ITEM.SOCURRENCY"),
            Num("quantity_on_hand", "ITEM.MTRL_ITEMTRDATA_QTY1"),
            Date("created_at", "ITEM.INSDATE"),
            Date("updated_at", "ITEM.UPDDATE")
        ]),

        new(ProjectsRead, Soft1ReadMode.Browser, "PRJC",
        [
            Key("project_id"),
            Text("project_code", "PRJC.CODE"),
            Text("name", "PRJC.NAME"),
            Id("customer_id", "PRJC.TRDR"),
            Col("customer_name", Soft1ValueKind.Label, "PRJC.TRDR", "=PRJC.TRDR_CUSTOMER_NAME"),
            Label("status", "PRJC.PRJSTATE"),
            Label("stage", "PRJC.PRJCSTAGE"),
            Id("manager_id", "PRJC.SALESMAN"),
            Date("start_date", "PRJC.FROMDATE"),
            Date("target_date", "PRJC.FINALDATE"),
            Date("completion_date", "PRJC.CLOSEDATE"),
            Label("category", "PRJC.PRJCATEGORY"),
            Flag("active", "PRJC.ISACTIVE")
        ]),

        Documents(SalesHeadersRead, "SALDOC", "CUSTOMER", customers: true),
        Documents(PurchaseHeadersRead, "PURDOC", "SUPPLIER", customers: false),

        Table(SeriesRead, "SERIES", "COMPANY", "{company}", "company",
            Text("series_id", "SERIES"), Text("code", "CODE"), Text("name", "NAME"), Num("module", "SOSOURCE"),
            Text("document_type_id", "FPRMS"), Num("behaviour", "TFPRMS"), Flag("active", "ISACTIVE"), Text("company", "COMPANY")),

        Table(DocumentTypesRead, "FPRMS", "COMPANY", "{company}", "company",
            Text("document_type_id", "FPRMS"), Text("name", "NAME"), Num("module", "SOSOURCE"), Num("behaviour", "TFPRMS"),
            Flag("is_delivery_note", "ISDELIVERYNOTE"), Flag("active", "ISACTIVE"), Text("company", "COMPANY")),

        // FLG01/FLG02 are the debit/credit flags of a transaction type on the trader's account — the sign rule
        // Soft1's own balance SQL applies to TRDTRN.TRNVAL.
        Table(TransactionTypesRead, "TPRMS", "COMPANY", "{company}", "company",
            Num("entity", "SODTYPE"), Text("transaction_type_id", "TPRMS"), Text("name", "NAME"), Flag("debit_flag", "FLG01"),
            Flag("credit_flag", "FLG02"), Text("company", "COMPANY")),

        Table(WarehousesRead, "WHOUSE", "COMPANY", "{company}", "company",
            Text("warehouse_id", "WHOUSE"), Text("warehouse_code", "SHORTCUT"), Text("name", "NAME"), Text("location", "CITY"),
            Text("address", "ADDRESS"), Flag("active", "ISACTIVE"), Text("company", "COMPANY")),

        Table(BranchesRead, "BRANCH", "COMPANY", "{company}", "company",
            Text("branch_id", "BRANCH"), Text("branch_code", "SHORTCUT"), Text("name", "NAME"), Text("company_id", "COMPANY"),
            Text("location", "CITY"), Flag("active", "ISACTIVE"), Text("company", "COMPANY")),

        Table(CompaniesRead, "COMPANY", "COMPANY", "{company}", null,
            Text("company_id", "COMPANY"), Text("name", "NAME"), Text("vat_number", "AFM"), Text("currency_id", "SOCURRENCY"),
            Text("country_id", "COUNTRY"), Flag("active", "ISACTIVE")),

        // Currencies, VAT codes and banks have no company column; ISACTIVE is the one key they share.
        Table(CurrenciesRead, "SOCURRENCY", "ISACTIVE", "1", null,
            Text("currency_id", "SOCURRENCY"), Text("code", "SHORTCUT"), Text("name", "NAME"), Text("international_code", "INTERCODE"),
            Flag("active", "ISACTIVE")),

        Table(PaymentTermsRead, "PAYMENT", "COMPANY", "{company}", "company",
            Text("payment_term_id", "PAYMENT"), Text("code", "CODE"), Text("name", "NAME"), Num("entity", "SODTYPE"),
            Num("instalments", "INSTALMENTS"), Flag("active", "ISACTIVE"), Text("company", "COMPANY")),

        Table(TaxCodesRead, "VAT", "ISACTIVE", "1", null,
            Text("tax_code_id", "VAT"), Text("name", "NAME"), Num("rate", "PERCNT"), Flag("active", "ISACTIVE")),

        Table(UnitsRead, "MTRUNIT", "COMPANY", "{company}", "company",
            Text("unit_id", "MTRUNIT"), Text("code", "SHORTCUT"), Text("name", "NAME"), Flag("active", "ISACTIVE"), Text("company", "COMPANY")),

        Table(ItemCategoriesRead, "MTRCATEGORY", "COMPANY", "{company}", "company",
            Text("category_id", "MTRCATEGORY"), Text("code", "CODE"), Text("name", "NAME"), Num("entity", "SODTYPE"),
            Flag("active", "ISACTIVE"), Text("company", "COMPANY")),

        Table(TraderCategoriesRead, "TRDCATEGORY", "COMPANY", "{company}", "company",
            Text("category_id", "TRDCATEGORY"), Text("code", "CODE"), Text("name", "NAME"), Num("entity", "SODTYPE"),
            Flag("active", "ISACTIVE"), Text("company", "COMPANY")),

        // G.L. accounts are entity 89; ACNT carries a chart-of-accounts schema rather than a company.
        Table(AccountsRead, "ACNT", "SODTYPE", "89", null,
            Text("account_id", "ACNT"), Text("account_code", "CODE"), Text("account_name", "NAME"), Text("account_type", "ACNTYPE"),
            Text("account_group", "ACNGROUP"), Text("account_category", "ACNCATEGORY"), Text("currency_id", "SOCURRENCY"),
            Flag("active", "ISACTIVE")),

        Table(PersonsRead, "PRSN", "COMPANY", "{company}", "company",
            Text("person_id", "PRSN"), Text("code", "CODE"), Text("name", "NAME"), Text("first_name", "NAME2"), Text("email", "EMAIL"),
            Text("department", "DEPART"), Flag("active", "ISACTIVE"), Text("company", "COMPANY")),

        // A person's roles: entity 30 is salesperson (31 collector, 32 buyer, 33 technician).
        Table(PersonRolesRead, "PRSEXT", "COMPANY", "{company}", "company",
            Text("person_id", "PRSN"), Num("entity", "SODTYPE"), Flag("active", "ISACTIVE"), Text("company", "COMPANY")),

        // The running balance of every trader, in company currency (LBAL) and the trader's own (TBAL).
        Table(TraderBalancesRead, "TRDFINDATA", "COMPANY", "{company}", "company",
            Text("trader_id", "TRDR"), Num("balance_local", "LBAL"), Num("balance", "TBAL"), Text("company", "COMPANY")),

        // Which entity each trader id is (13 customer, 12 supplier, 14 cash account) — the ledger tables name a
        // trader without saying which kind it is.
        Table(TradersRead, "TRDR", "COMPANY", "{company}", "company",
            Text("trader_id", "TRDR"), Num("entity", "SODTYPE"), Text("code", "CODE"), Text("name", "NAME"), Text("company", "COMPANY")),

        Table(CashAccountsRead, "TRDR", "SODTYPE", "14", "company",
            Text("account_id", "TRDR"), Text("account_code", "CODE"), Text("account_name", "NAME"), Text("currency_id", "SOCURRENCY"),
            Flag("active", "ISACTIVE"), Date("last_update", "UPDDATE"), Text("company", "COMPANY")),

        Table(TraderBankAccountsRead, "TRDBANKACC", "COMPANY", "{company}", "company",
            Text("trader_id", "TRDR"), Text("bank_id", "BANK"), Text("iban", "IBAN"), Text("account_number", "BANKACNNUM"),
            Num("line_number", "LINENUM"), Text("company", "COMPANY")),

        Table(BanksRead, "BANK", "ISACTIVE", "1", null, Text("bank_id", "BANK"), Text("name", "NAME")),

        // The trader ledger. TRNVAL is in the document's currency, LTRNVAL in the company's.
        Table(TraderTransactionsRead, "TRDTRN", "FISCPRD", "{fiscal_year}", "company",
            Text("transaction_id", "TRDTRN"), Text("document_id", "FINDOC"), Text("trader_id", "TRDR"), Num("entity", "SODTYPE"),
            Date("transaction_date", "TRNDATE"), Text("document_number", "FINCODE"), Text("reference", "COMMENTS"),
            Text("transaction_type_id", "TPRMS"), Text("series_id", "SERIES"), Text("currency_id", "SOCURRENCY"),
            Num("value", "TRNVAL"), Num("value_local", "LTRNVAL"), Text("project_id", "PRJC"), Text("salesperson_id", "SALESMAN"),
            Num("module", "SOSOURCE"), Text("branch_id", "BRANCH"), Text("company", "COMPANY")),

        Table(LedgerLinesRead, "ACNTRN", "FISCPRD", "{fiscal_year}", "company",
            Text("entry_id", "ACNTRN"), Text("document_id", "ACNHEADER"), Date("posting_date", "TRNDATE"), Text("account_id", "ACNT"),
            Text("document_number", "FINCODE"), Text("description", "COMMENTS"), Num("debit", "DEBIT"), Num("credit", "CREDIT"),
            Num("debit_local", "LDEBIT"), Num("credit_local", "LCREDIT"), Text("currency_id", "SOCURRENCY"), Text("trader_id", "TRDR"),
            Text("project_id", "PRJC"), Text("branch_id", "BRANCH"), Flag("cancelled", "ISCANCEL"), Text("company", "COMPANY")),

        Table(LedgerBalancesRead, "ACNBALSHEET", "FISCPRD", "{fiscal_year}", "company",
            Text("account_id", "ACNT"), Num("fiscal_year", "FISCPRD"), Num("period_number", "PERIOD"), Num("debit", "LDEBIT"),
            Num("credit", "LCREDIT"), Text("company", "COMPANY")),

        Table(ItemMovementsRead, "MTRTRN", "FISCPRD", "{fiscal_year}", "company",
            Text("movement_id", "MTRTRN"), Text("document_id", "FINDOC"), Date("movement_date", "TRNDATE"), Text("item_id", "MTRL"),
            Text("warehouse_id", "WHOUSE"), Text("movement_type_id", "TPRMS"), Num("quantity", "QTY1"), Num("unit_price", "PRICE"),
            Num("value", "LTRNVAL"), Num("cost", "LCOSTVAL"), Text("trader_id", "TRDR"), Text("project_id", "PRJC"),
            Text("document_number", "FINCODE"), Num("module", "SOSOURCE"), Text("company", "COMPANY")),

        Table(ItemWarehouseBalancesRead, "MTRFINDATA", "FISCPRD", "{fiscal_year}", "company",
            Text("item_id", "MTRL"), Text("warehouse_id", "WHOUSE"), Num("quantity", "QTY1"), Num("value_in", "IMPVAL"),
            Num("value_out", "EXPVAL"), Num("fiscal_year", "FISCPRD"), Text("company", "COMPANY")),

        Table(ItemYearDataRead, "MTRDATA", "FISCPRD", "{fiscal_year}", "company",
            Text("item_id", "MTRL"), Num("last_purchase_price", "PURLPRICE"), Date("last_purchase_date", "PURLPDATE"),
            Text("company", "COMPANY")),

        Table(ItemSupplierCodesRead, "MTRSUPCODE", "COMPANY", "{company}", "company",
            Text("item_id", "MTRL"), Text("supplier_id", "TRDR"), Text("supplier_item_code", "MTRSUPCODE"),
            Text("supplier_item_name", "MTRSUPNAME"), Flag("active", "ISACTIVE"), Text("company", "COMPANY")),

        // Instalments not yet settled. FINPAYTERMS has no entity column; the trader's decides customer or supplier.
        Table(OpenItemsRead, "FINPAYTERMS", "ISCLOSE", "0", "company",
            Text("open_item_id", "FINPAYTERMS"), Text("document_id", "FINDOC"), Text("trader_id", "TRDR"),
            Text("currency_id", "SOCURRENCY"), Date("invoice_date", "TRNDATE"), Date("due_date", "FINALDATE"),
            Num("original_amount", "AMNT"), Num("outstanding_amount", "OPNTAMNT"), Flag("cancelled", "ISCANCEL"),
            Text("company", "COMPANY"))
    ];

    private IReadOnlyDictionary<string, Soft1Read>? resolved;

    public IReadOnlyCollection<Soft1Read> All => Resolve().Values.ToList();

    public Soft1Read Get(string id)
    {
        return Resolve().TryGetValue(id, out var read)
            ? read
            : throw new ArgumentException($"Unknown Soft1 read '{id}'.", nameof(id));
    }

    /// <summary>The built-in reads with <c>Soft1:Reads</c> applied. Options are read once; a change needs a restart.</summary>
    private IReadOnlyDictionary<string, Soft1Read> Resolve()
    {
        if (resolved is not null)
        {
            return resolved;
        }

        var overrides = options.Value.Reads;
        var result = new Dictionary<string, Soft1Read>(StringComparer.OrdinalIgnoreCase);

        foreach (var read in BuiltIn)
        {
            result[read.Id] = overrides.TryGetValue(read.Id, out var change) ? Apply(read, change) : read;
        }

        foreach (var id in overrides.Keys.Where(id => !result.ContainsKey(id)))
        {
            throw new InvalidOperationException(
                $"Soft1:Reads:{id} overrides a read that does not exist. Known: {string.Join(", ", result.Keys.Order())}.");
        }

        return resolved = result;
    }

    private static Soft1Read Apply(Soft1Read read, Soft1ReadOverride change)
    {
        var columns = read.Columns
            .Select(column => change.Columns.TryGetValue(column.Field, out var candidates)
                ? column with { Candidates = [.. candidates.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)] }
                : column)
            .ToList();

        foreach (var field in change.Columns.Keys.Where(field => read.Columns.All(column => !column.Field.Equals(field, StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidOperationException(
                $"Soft1:Reads:{read.Id}:Columns:{field} names a field this read does not have. " +
                $"Its fields: {string.Join(", ", read.Columns.Select(static column => column.Field))}.");
        }

        return read with
        {
            Mode = change.Mode ?? read.Mode,
            Object = Blank(change.Object) ?? read.Object,
            List = change.List ?? read.List,
            Filters = change.Filters ?? read.Filters,
            DateFilter = change.DateFilter is null ? read.DateFilter : Blank(change.DateFilter),
            SeriesFilter = change.SeriesFilter is null ? read.SeriesFilter : Blank(change.SeriesFilter),
            KeyName = change.KeyName ?? read.KeyName,
            KeyValue = change.KeyValue ?? read.KeyValue,
            Columns = columns
        };
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
