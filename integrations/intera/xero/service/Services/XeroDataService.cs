using Intera.Common.Infrastructure.Query;
using Intera.Common.Models.Query;
using Intera.Xero.Api.Clients;
using Intera.Xero.Api.Infrastructure;
using Intera.Xero.Api.Models;
using Intera.Xero.Api.Options;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;

namespace Intera.Xero.Api.Services;

using Row = Dictionary<string, object?>;
using Rows = IReadOnlyList<IReadOnlyDictionary<string, object?>>;

public interface IXeroDataService
{
    /// <summary>The rows of one data source for the organisation the request names, narrowed by what Xero can narrow.</summary>
    Task<Rows> LoadAsync(XeroSource source, QueryRequest? query, string? tenantId, CancellationToken cancellationToken);
}

/// <summary>
/// Builds the rows of every data source out of the Accounting API: lists flattened to one row per record or per line,
/// reports flattened to one row per report line and period.
/// </summary>
/// <remarks>
/// <para>
/// <b>Written against Xero's OpenAPI contract and documentation, not yet against a live organisation</b> — the
/// project's live verification covers the remaining edge cases on the Demo Company.
/// </para>
/// <para>
/// Every row carries the five metadata fields (<see cref="XeroSources.Metadata"/>); <c>intera_synced_at</c> is when
/// the answer the row came from was fetched, which a cached answer keeps.
/// </para>
/// <para>
/// Scoped: the organisation, the chart of accounts and the tracking categories one query needs are read once per
/// request (and cached across requests by <see cref="XeroClient"/>).
/// </para>
/// </remarks>
public sealed partial class XeroDataService(
    XeroClient client,
    IOptionsMonitor<XeroOptions> options,
    ILogger<XeroDataService> logger) : IXeroDataService
{
    private XeroOptions Options => options.CurrentValue;

    public async Task<Rows> LoadAsync(XeroSource source, QueryRequest? query, string? tenantId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var tenant = await client.ResolveTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var organisation = await client.GetAsync(tenant, "Organisation", null, reference: true, cancellationToken).ConfigureAwait(false);
        var context = new ReadContext(
            tenant,
            XeroJson.First(organisation.Root, "Organisations") ?? default,
            organisation.FetchedAt,
            query,
            source,
            Today(query));

        IReadOnlyList<Row> rows = source.Id switch
        {
            XeroSources.Organisation => LoadOrganisation(context),
            XeroSources.Accounts => await LoadAccountsAsync(context, cancellationToken).ConfigureAwait(false),
            XeroSources.Contacts => await LoadContactsAsync(context, cancellationToken).ConfigureAwait(false),
            XeroSources.TrackingCategories => await LoadTrackingCategoriesAsync(context, cancellationToken).ConfigureAwait(false),
            XeroSources.TrackingOptions => await LoadTrackingOptionsAsync(context, cancellationToken).ConfigureAwait(false),
            XeroSources.TaxRates => await LoadTaxRatesAsync(context, cancellationToken).ConfigureAwait(false),
            XeroSources.Currencies => await LoadCurrenciesAsync(context, cancellationToken).ConfigureAwait(false),

            XeroSources.Invoices => await LoadInvoicesAsync(context, lines: false, cancellationToken).ConfigureAwait(false),
            XeroSources.InvoiceLines => await LoadInvoicesAsync(context, lines: true, cancellationToken).ConfigureAwait(false),
            XeroSources.Payments => await LoadPaymentsAsync(context, cancellationToken).ConfigureAwait(false),
            XeroSources.BankTransactions => await LoadBankTransactionsAsync(context, lines: false, cancellationToken).ConfigureAwait(false),
            XeroSources.BankTransactionLines => await LoadBankTransactionsAsync(context, lines: true, cancellationToken).ConfigureAwait(false),
            XeroSources.BankTransfers => await LoadBankTransfersAsync(context, cancellationToken).ConfigureAwait(false),
            XeroSources.CreditNotes => await LoadCreditNotesAsync(context, lines: false, cancellationToken).ConfigureAwait(false),
            XeroSources.CreditNoteLines => await LoadCreditNotesAsync(context, lines: true, cancellationToken).ConfigureAwait(false),
            XeroSources.ManualJournals => await LoadManualJournalsAsync(context, lines: false, cancellationToken).ConfigureAwait(false),
            XeroSources.ManualJournalLines => await LoadManualJournalsAsync(context, lines: true, cancellationToken).ConfigureAwait(false),
            XeroSources.Budgets => await LoadBudgetsAsync(context, cancellationToken).ConfigureAwait(false),
            XeroSources.BudgetLines => await LoadBudgetLinesAsync(context, cancellationToken).ConfigureAwait(false),

            XeroSources.ProfitAndLoss => await LoadProfitAndLossAsync(context, cancellationToken).ConfigureAwait(false),
            XeroSources.BalanceSheet => await LoadBalanceSheetAsync(context, cancellationToken).ConfigureAwait(false),
            XeroSources.TrialBalance => await LoadTrialBalanceAsync(context, cancellationToken).ConfigureAwait(false),
            XeroSources.BankSummary => await LoadBankSummaryAsync(context, cancellationToken).ConfigureAwait(false),
            XeroSources.BudgetSummary => await LoadBudgetSummaryAsync(context, cancellationToken).ConfigureAwait(false),
            XeroSources.AgedReceivablesByContact => await LoadAgedAsync(context, "AgedReceivablesByContact", cancellationToken).ConfigureAwait(false),
            XeroSources.AgedPayablesByContact => await LoadAgedAsync(context, "AgedPayablesByContact", cancellationToken).ConfigureAwait(false),

            _ => throw new ArgumentException($"Unknown Xero data source '{source.Id}'.")
        };

        return [.. rows.Select(row => XeroSources.Project(source, row))];
    }

    /// <param name="Organisation">The <c>Organisations[0]</c> object; <c>Undefined</c> if Xero sent none.</param>
    /// <param name="Today">Today in the query's time zone (UTC without one) — what the derived and default dates count from.</param>
    private sealed record ReadContext(
        XeroConnection Tenant,
        JsonElement Organisation,
        DateTime OrganisationFetchedAt,
        QueryRequest? Query,
        XeroSource Source,
        DateOnly Today)
    {
        public string? OrganisationId => Organisation.ValueKind == JsonValueKind.Object ? XeroJson.String(Organisation, "OrganisationID") : null;
    }

    // ---- Reference data -------------------------------------------------------------------------------------

    private static List<Row> LoadOrganisation(ReadContext context)
    {
        var o = context.Organisation;
        if (o.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return
        [
            Stamp(context, new Row
            {
                ["organisation_id"] = XeroJson.String(o, "OrganisationID"),
                ["name"] = XeroJson.String(o, "Name"),
                ["legal_name"] = XeroJson.String(o, "LegalName"),
                ["organisation_type"] = XeroJson.String(o, "OrganisationType"),
                ["organisation_class"] = XeroJson.String(o, "Class"),
                ["edition"] = XeroJson.String(o, "Edition"),
                ["organisation_status"] = XeroJson.String(o, "OrganisationStatus"),
                ["country_code"] = XeroJson.String(o, "CountryCode"),
                ["base_currency"] = XeroJson.String(o, "BaseCurrency"),
                ["timezone"] = XeroJson.String(o, "Timezone"),
                ["is_demo_company"] = XeroJson.Boolean(o, "IsDemoCompany"),
                ["financial_year_end_day"] = XeroJson.Integer(o, "FinancialYearEndDay"),
                ["financial_year_end_month"] = XeroJson.Integer(o, "FinancialYearEndMonth"),
                ["sales_tax_basis"] = XeroJson.String(o, "SalesTaxBasis"),
                ["sales_tax_period"] = XeroJson.String(o, "SalesTaxPeriod"),
                ["period_lock_date"] = XeroJson.Date(o, "PeriodLockDate"),
                ["end_of_year_lock_date"] = XeroJson.Date(o, "EndOfYearLockDate"),
                ["shortcode"] = XeroJson.String(o, "ShortCode"),
                ["line_of_business"] = XeroJson.String(o, "LineOfBusiness"),
                ["created_at"] = XeroJson.Timestamp(o, "CreatedDateUTC")
            }, updatedAt: null, context.OrganisationFetchedAt)
        ];
    }

    private async Task<List<Row>> LoadAccountsAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var accounts = await ReadAccountsAsync(context, cancellationToken).ConfigureAwait(false);

        return
        [
            .. accounts.Items.Select(a => Stamp(context, new Row
            {
                ["account_id"] = XeroJson.String(a, "AccountID"),
                ["code"] = XeroJson.String(a, "Code"),
                ["name"] = XeroJson.String(a, "Name"),
                ["description"] = XeroJson.String(a, "Description"),
                ["type"] = XeroJson.String(a, "Type"),
                ["class"] = XeroJson.String(a, "Class"),
                ["status"] = XeroJson.String(a, "Status"),
                ["system_account"] = XeroJson.String(a, "SystemAccount"),
                ["tax_type"] = XeroJson.String(a, "TaxType"),
                ["bank_account_number"] = XeroJson.String(a, "BankAccountNumber"),
                ["bank_account_type"] = XeroJson.String(a, "BankAccountType"),
                ["currency_code"] = XeroJson.String(a, "CurrencyCode"),
                ["enable_payments"] = XeroJson.Boolean(a, "EnablePaymentsToAccount"),
                ["show_in_expense_claims"] = XeroJson.Boolean(a, "ShowInExpenseClaims"),
                ["reporting_code"] = XeroJson.String(a, "ReportingCode"),
                ["reporting_code_name"] = XeroJson.String(a, "ReportingCodeName"),
                ["updated_at"] = XeroJson.Timestamp(a, "UpdatedDateUTC")
            }, XeroJson.Timestamp(a, "UpdatedDateUTC"), accounts.FetchedAt))
        ];
    }

    private async Task<List<Row>> LoadContactsAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var contacts = await client.GetCollectionAsync(
            context.Tenant,
            "Contacts",
            "Contacts",
            new Dictionary<string, string?> { ["includeArchived"] = "true" },
            paged: true,
            reference: false,
            cancellationToken).ConfigureAwait(false);

        return [.. contacts.Items.Select(contact => ContactRow(context, contact, contacts.FetchedAt))];
    }

    private static Row ContactRow(ReadContext context, JsonElement c, DateTime fetchedAt)
    {
        var balances = XeroJson.Object(c, "Balances");
        var receivable = balances is { } b1 ? XeroJson.Object(b1, "AccountsReceivable") : null;
        var payable = balances is { } b2 ? XeroJson.Object(b2, "AccountsPayable") : null;

        return Stamp(context, new Row
        {
            ["contact_id"] = XeroJson.String(c, "ContactID"),
            ["contact_number"] = XeroJson.String(c, "ContactNumber"),
            ["account_number"] = XeroJson.String(c, "AccountNumber"),
            ["name"] = XeroJson.String(c, "Name"),
            ["first_name"] = XeroJson.String(c, "FirstName"),
            ["last_name"] = XeroJson.String(c, "LastName"),
            ["email"] = XeroJson.String(c, "EmailAddress"),
            ["company_number"] = XeroJson.String(c, "CompanyNumber"),
            ["tax_number"] = XeroJson.String(c, "TaxNumber"),
            ["status"] = XeroJson.String(c, "ContactStatus"),
            ["is_customer"] = XeroJson.Boolean(c, "IsCustomer"),
            ["is_supplier"] = XeroJson.Boolean(c, "IsSupplier"),
            ["currency"] = XeroJson.String(c, "DefaultCurrency"),
            ["accounts_receivable_outstanding"] = XeroJson.Decimal(receivable, "Outstanding"),
            ["accounts_receivable_overdue"] = XeroJson.Decimal(receivable, "Overdue"),
            ["accounts_payable_outstanding"] = XeroJson.Decimal(payable, "Outstanding"),
            ["accounts_payable_overdue"] = XeroJson.Decimal(payable, "Overdue"),
            ["updated_at"] = XeroJson.Timestamp(c, "UpdatedDateUTC")
        }, XeroJson.Timestamp(c, "UpdatedDateUTC"), fetchedAt);
    }

    private async Task<List<Row>> LoadTrackingCategoriesAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var categories = await ReadTrackingCategoriesAsync(context, cancellationToken).ConfigureAwait(false);

        return
        [
            .. categories.Items.Select(category => Stamp(context, new Row
            {
                ["tracking_category_id"] = XeroJson.String(category, "TrackingCategoryID"),
                ["name"] = XeroJson.String(category, "Name"),
                ["status"] = XeroJson.String(category, "Status")
            }, updatedAt: null, categories.FetchedAt))
        ];
    }

    private async Task<List<Row>> LoadTrackingOptionsAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var categories = await ReadTrackingCategoriesAsync(context, cancellationToken).ConfigureAwait(false);

        return
        [
            .. categories.Items.SelectMany(category => XeroJson.Array(category, "Options").Select(option => Stamp(context, new Row
            {
                ["tracking_category_id"] = XeroJson.String(category, "TrackingCategoryID"),
                ["tracking_category_name"] = XeroJson.String(category, "Name"),
                ["tracking_option_id"] = XeroJson.String(option, "TrackingOptionID"),
                ["name"] = XeroJson.String(option, "Name"),
                ["status"] = XeroJson.String(option, "Status")
            }, updatedAt: null, categories.FetchedAt)))
        ];
    }

    private async Task<List<Row>> LoadTaxRatesAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var rates = await client.GetCollectionAsync(context.Tenant, "TaxRates", "TaxRates", null, paged: false, reference: true, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rates.Items.Select(rate => Stamp(context, new Row
            {
                ["tax_type"] = XeroJson.String(rate, "TaxType"),
                ["name"] = XeroJson.String(rate, "Name"),
                ["status"] = XeroJson.String(rate, "Status"),
                ["report_tax_type"] = XeroJson.String(rate, "ReportTaxType"),
                ["display_tax_rate"] = XeroJson.Decimal(rate, "DisplayTaxRate"),
                ["effective_rate"] = XeroJson.Decimal(rate, "EffectiveRate"),
                ["can_apply_to_assets"] = XeroJson.Boolean(rate, "CanApplyToAssets"),
                ["can_apply_to_equity"] = XeroJson.Boolean(rate, "CanApplyToEquity"),
                ["can_apply_to_expenses"] = XeroJson.Boolean(rate, "CanApplyToExpenses"),
                ["can_apply_to_liabilities"] = XeroJson.Boolean(rate, "CanApplyToLiabilities"),
                ["can_apply_to_revenue"] = XeroJson.Boolean(rate, "CanApplyToRevenue")
            }, updatedAt: null, rates.FetchedAt))
        ];
    }

    private async Task<List<Row>> LoadCurrenciesAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var currencies = await client.GetCollectionAsync(context.Tenant, "Currencies", "Currencies", null, paged: false, reference: true, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. currencies.Items.Select(currency => Stamp(context, new Row
            {
                ["code"] = XeroJson.String(currency, "Code"),
                ["description"] = XeroJson.String(currency, "Description")
            }, updatedAt: null, currencies.FetchedAt))
        ];
    }

    // ---- Shared reads ---------------------------------------------------------------------------------------

    private Task<XeroCollection> ReadAccountsAsync(ReadContext context, CancellationToken cancellationToken) =>
        client.GetCollectionAsync(context.Tenant, "Accounts", "Accounts", null, paged: false, reference: true, cancellationToken);

    private Task<XeroCollection> ReadTrackingCategoriesAsync(ReadContext context, CancellationToken cancellationToken) =>
        client.GetCollectionAsync(
            context.Tenant,
            "TrackingCategories",
            "TrackingCategories",
            new Dictionary<string, string?> { ["includeArchived"] = "true" },
            paged: false,
            reference: true,
            cancellationToken);

    /// <summary>Account id → (code, name), for the lines and reports that carry only the id.</summary>
    private async Task<Dictionary<string, (string? Code, string? Name)>> ReadAccountIndexAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var accounts = await ReadAccountsAsync(context, cancellationToken).ConfigureAwait(false);
        var index = new Dictionary<string, (string? Code, string? Name)>(StringComparer.OrdinalIgnoreCase);

        foreach (var account in accounts.Items)
        {
            if (XeroJson.String(account, "AccountID") is { } id)
            {
                index[id] = (XeroJson.String(account, "Code"), XeroJson.String(account, "Name"));
            }
        }

        return index;
    }

    // ---- Helpers --------------------------------------------------------------------------------------------

    /// <summary>Adds the five metadata fields every source ends with.</summary>
    private static Row Stamp(ReadContext context, Row row, DateTime? updatedAt, DateTime fetchedAt)
    {
        row[XeroSources.TenantIdField] = context.Tenant.TenantId;
        row[XeroSources.OrganisationIdField] = context.OrganisationId;
        row[XeroSources.SourceSystemField] = "xero";
        row[XeroSources.SourceUpdatedAtField] = updatedAt;
        row[XeroSources.SyncedAtField] = fetchedAt;
        return row;
    }

    /// <summary>The bounds the filter's top-level AND chain puts on <paramref name="field"/>.</summary>
    private static (DateOnly? From, DateOnly? To) Window(QueryRequest? query, string? field)
    {
        if (field is null)
        {
            return (null, null);
        }

        var pushdown = QueryPushdownExtractor.Extract(query, field, []);
        return (pushdown.From, pushdown.To);
    }

    /// <summary>The values the filter names for <paramref name="field"/> through = / IN, or empty.</summary>
    private static IReadOnlyList<string> Keys(QueryRequest? query, string field) =>
        QueryPushdownExtractor.Extract(query, null, [field]).Values(field);

    /// <summary>
    /// Only ids that are GUIDs go to Xero: anything else could never match one, and the filter still runs in memory
    /// over what comes back, so dropping the pushdown loses nothing.
    /// </summary>
    private static IReadOnlyList<string>? GuidKeys(QueryRequest? query, string field)
    {
        var values = Keys(query, field);
        return values.Count > 0 && values.All(static value => Guid.TryParse(value, out _)) ? values : null;
    }

    /// <summary>A Xero <c>where</c> on a date property, inclusive on both ends.</summary>
    private static string? DateWhere(string property, DateOnly? from, DateOnly? to)
    {
        var parts = new List<string>(2);
        if (from is { } lower)
        {
            parts.Add($"{property} >= DateTime({lower.Year}, {lower.Month:00}, {lower.Day:00})");
        }

        if (to is { } upper)
        {
            parts.Add($"{property} <= DateTime({upper.Year}, {upper.Month:00}, {upper.Day:00})");
        }

        return parts.Count == 0 ? null : string.Join(" AND ", parts);
    }

    private static string? And(params string?[] clauses)
    {
        var present = clauses.Where(static clause => !string.IsNullOrEmpty(clause)).ToList();
        return present.Count == 0 ? null : string.Join(" AND ", present);
    }

    private static DateOnly Today(QueryRequest? query)
    {
        var zone = RelativeDateResolver.FindTimeZone(query?.TimeZone);
        var now = zone is null ? DateTime.UtcNow : TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime;
        return DateOnly.FromDateTime(now);
    }

    private static DateTime ToDateTime(DateOnly date) => date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

    private static string IsoDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The first day of the financial year <paramref name="date"/> falls in, per the organisation's year end.</summary>
    private static DateOnly FinancialYearStart(ReadContext context, DateOnly date)
    {
        var o = context.Organisation;
        var endMonth = o.ValueKind == JsonValueKind.Object ? XeroJson.Integer(o, "FinancialYearEndMonth") : null;
        var endDay = o.ValueKind == JsonValueKind.Object ? XeroJson.Integer(o, "FinancialYearEndDay") : null;

        if (endMonth is not { } month || month is < 1 or > 12 || endDay is not { } day || day < 1)
        {
            return new DateOnly(date.Year, 1, 1);
        }

        DateOnly EndIn(int year) => new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));

        var end = EndIn(date.Year);
        if (date > end)
        {
            end = EndIn(date.Year + 1);
        }

        return EndIn(end.Year - 1).AddDays(1);
    }
}
