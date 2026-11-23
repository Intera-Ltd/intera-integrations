using Intera.Common.Infrastructure.Query;
using System.Text.Json.Serialization;

namespace Intera.Xero.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter<XeroFieldType>))]
public enum XeroFieldType
{
    Text,
    Number,
    Date,
    Flag
}

/// <param name="Lookup">Source id whose rows name this id, for the plugin's field lookup.</param>
/// <param name="LookupValue">Field of <paramref name="Lookup"/> holding the id.</param>
/// <param name="LookupLabel">Field of <paramref name="Lookup"/> holding the label.</param>
public sealed record XeroField(
    string Name,
    XeroFieldType Type,
    string Label,
    string? Lookup = null,
    string? LookupValue = null,
    string? LookupLabel = null);

/// <param name="DateField">
/// The field whose bounds in the filter are passed to Xero — as a <c>where</c> on the date for transactions, as the
/// report's dates for reports — so that only that window is read.
/// </param>
public sealed record XeroSource(string Id, string Name, string Description, string? DateField, IReadOnlyList<XeroField> Fields)
{
    [JsonIgnore]
    public IReadOnlyDictionary<string, InMemoryQueryField> Allowlist { get; } =
        InMemoryQueryFieldsBuilder.Build([.. Fields.Select(static field => field.Name)]);
}

/// <summary>
/// Every data source this API serves: its id, its fields and their types. The one list the allowlists, the row
/// projection and the plugin's field descriptors (<c>GET /api/Schema/sources</c>) are all built from, so the three
/// cannot drift.
/// </summary>
/// <remarks>
/// Every source ends with the same five metadata fields (<see cref="Metadata"/>): which organisation the row came
/// from, that it came from Xero, when Xero last changed it, and when this service read it.
/// </remarks>
public static class XeroSources
{
    public const string Organisation = "organisation";
    public const string Accounts = "accounts";
    public const string Contacts = "contacts";
    public const string Invoices = "invoices";
    public const string InvoiceLines = "invoice_lines";
    public const string Payments = "payments";
    public const string BankTransactions = "bank_transactions";
    public const string BankTransactionLines = "bank_transaction_lines";
    public const string BankTransfers = "bank_transfers";
    public const string CreditNotes = "credit_notes";
    public const string CreditNoteLines = "credit_note_lines";
    public const string ManualJournals = "manual_journals";
    public const string ManualJournalLines = "manual_journal_lines";
    public const string TrackingCategories = "tracking_categories";
    public const string TrackingOptions = "tracking_options";
    public const string TaxRates = "tax_rates";
    public const string Currencies = "currencies";
    public const string ProfitAndLoss = "profit_and_loss";
    public const string BalanceSheet = "balance_sheet";
    public const string TrialBalance = "trial_balance";
    public const string BankSummary = "bank_summary";
    public const string AgedReceivablesByContact = "aged_receivables_by_contact";
    public const string AgedPayablesByContact = "aged_payables_by_contact";
    public const string Budgets = "budgets";
    public const string BudgetLines = "budget_lines";
    public const string BudgetSummary = "budget_summary";

    public const string TenantIdField = "xero_tenant_id";
    public const string OrganisationIdField = "xero_organisation_id";
    public const string SourceSystemField = "source_system";
    public const string SourceUpdatedAtField = "source_updated_at";
    public const string SyncedAtField = "intera_synced_at";

    private static XeroField T(string name, string label) => new(name, XeroFieldType.Text, label);

    private static XeroField N(string name, string label) => new(name, XeroFieldType.Number, label);

    private static XeroField D(string name, string label) => new(name, XeroFieldType.Date, label);

    private static XeroField F(string name, string label) => new(name, XeroFieldType.Flag, label);

    private static XeroField Account(string name = "account_id", string label = "Account ID") =>
        new(name, XeroFieldType.Text, label, Accounts, "account_id", "name");

    private static XeroField Contact(string name = "contact_id", string label = "Contact ID") =>
        new(name, XeroFieldType.Text, label, Contacts, "contact_id", "name");

    private static XeroField Invoice() => new("invoice_id", XeroFieldType.Text, "Invoice ID", Invoices, "invoice_id", "invoice_number");

    private static XeroField Budget() => new("budget_id", XeroFieldType.Text, "Budget ID", Budgets, "budget_id", "description");

    private static XeroField Currency(string name = "currency_code", string label = "Currency") =>
        new(name, XeroFieldType.Text, label, Currencies, "code", "description");

    private static XeroField[] Tracking(int slot) =>
    [
        new($"tracking_category_{slot}_id", XeroFieldType.Text, $"Tracking category {slot} ID", TrackingCategories, "tracking_category_id", "name"),
        T($"tracking_category_{slot}", $"Tracking category {slot}"),
        new($"tracking_option_{slot}_id", XeroFieldType.Text, $"Tracking option {slot} ID", TrackingOptions, "tracking_option_id", "name"),
        T($"tracking_option_{slot}", $"Tracking option {slot}")
    ];

    private static XeroField[] Line(bool item, bool discount) =>
    [
        T("line_item_id", "Line item ID"),
        T("description", "Description"),
        N("quantity", "Quantity"),
        N("unit_amount", "Unit amount"),
        N("line_amount", "Line amount"),
        .. discount ? (XeroField[])[N("discount_rate", "Discount rate, %"), N("discount_amount", "Discount amount")] : [],
        Account(),
        T("account_code", "Account code"),
        .. item ? (XeroField[])[T("item_code", "Item code")] : [],
        T("tax_type", "Tax type"),
        N("tax_amount", "Tax amount"),
        .. Tracking(1),
        .. Tracking(2)
    ];

    /// <summary>The rows of a report laid out by account: P&amp;L, balance sheet, budget summary.</summary>
    private static XeroField[] ReportLine() =>
    [
        T("section", "Section"),
        T("row_type", "Row type"),
        T("label", "Label"),
        Account(),
        T("account_code", "Account code"),
        T("account_name", "Account name"),
        N("value", "Value")
    ];

    private static XeroField[] Aged(string direction) =>
    [
        D("report_date", "Report date"),
        Contact(),
        T("contact_name", "Contact"),
        T("row_type", "Row type"),
        Invoice(),
        T("invoice_number", "Invoice number"),
        D("invoice_date", "Invoice date"),
        D("due_date", "Due date"),
        N("days_overdue", "Days overdue"),
        T("ageing_bucket", "Ageing bucket"),
        N("total", "Total"),
        N("paid", direction == "receivable" ? "Received" : "Paid"),
        N("credited", "Credited"),
        N("amount_due", "Amount due"),
        D("report_generated_at", "Report generated")
    ];

    /// <summary>The five fields every source ends with.</summary>
    public static readonly IReadOnlyList<XeroField> Metadata =
    [
        T(TenantIdField, "Xero tenant ID"),
        T(OrganisationIdField, "Xero organisation ID"),
        T(SourceSystemField, "Source system"),
        D(SourceUpdatedAtField, "Updated in Xero"),
        D(SyncedAtField, "Read from Xero")
    ];

    private static XeroSource Source(string id, string name, string description, string? dateField, IEnumerable<XeroField> fields) =>
        new(id, name, description, dateField, [.. fields, .. Metadata]);

    public static readonly IReadOnlyList<XeroSource> All =
    [
        Source(Organisation, "Organisation",
            "The organisation itself (`GET /Organisation`): identity, base currency, financial year, tax basis and lock dates.", null,
            [
                T("organisation_id", "Organisation ID"), T("name", "Name"), T("legal_name", "Legal name"),
                T("organisation_type", "Organisation type"), T("organisation_class", "Class"), T("edition", "Edition"),
                T("organisation_status", "Status"), T("country_code", "Country"), Currency("base_currency", "Base currency"),
                T("timezone", "Time zone"), F("is_demo_company", "Demo company"),
                N("financial_year_end_day", "Financial year end day"), N("financial_year_end_month", "Financial year end month"),
                T("sales_tax_basis", "Sales tax basis"), T("sales_tax_period", "Sales tax period"),
                D("period_lock_date", "Period lock date"), D("end_of_year_lock_date", "End of year lock date"),
                T("shortcode", "Short code"), T("line_of_business", "Line of business"), D("created_at", "Created")
            ]),

        Source(Accounts, "Accounts",
            "The chart of accounts (`GET /Accounts`); `class` is ASSET / LIABILITY / EQUITY / REVENUE / EXPENSE, `type` the finer kind (BANK, CURRENT, SALES, OVERHEADS, …).", null,
            [
                Account(), T("code", "Code"), T("name", "Name"), T("description", "Description"), T("type", "Type"),
                T("class", "Class"), T("status", "Status"), T("system_account", "System account"), T("tax_type", "Tax type"),
                T("bank_account_number", "Bank account number"), T("bank_account_type", "Bank account type"),
                Currency(), F("enable_payments", "Payments enabled"), F("show_in_expense_claims", "Shown in expense claims"),
                T("reporting_code", "Reporting code"), T("reporting_code_name", "Reporting code name"), D("updated_at", "Updated")
            ]),

        Source(Contacts, "Contacts",
            "Customers and suppliers (`GET /Contacts`, archived included), with their outstanding and overdue balances. Join on `contact_id` — Xero does not keep names unique.", null,
            [
                Contact(), T("contact_number", "Contact number"), T("account_number", "Account number"), T("name", "Name"),
                T("first_name", "First name"), T("last_name", "Last name"), T("email", "Email"),
                T("company_number", "Company number"), T("tax_number", "Tax number"), T("status", "Status"),
                F("is_customer", "Customer"), F("is_supplier", "Supplier"), Currency("currency", "Default currency"),
                N("accounts_receivable_outstanding", "Receivable outstanding"), N("accounts_receivable_overdue", "Receivable overdue"),
                N("accounts_payable_outstanding", "Payable outstanding"), N("accounts_payable_overdue", "Payable overdue"),
                D("updated_at", "Updated")
            ]),

        Source(Invoices, "Invoices",
            "Sales invoices (`invoice_type` ACCREC) and supplier bills (ACCPAY) from `GET /Invoices`. `days_to_due`, `days_overdue`, `is_overdue`, `outstanding_ratio` and `document_direction` are derived here, against today in the query's time zone.", "invoice_date",
            [
                Invoice(), T("invoice_type", "Invoice type"), T("invoice_number", "Invoice number"), T("reference", "Reference"),
                Contact(), T("contact_name", "Contact"), D("invoice_date", "Invoice date"), D("due_date", "Due date"),
                T("status", "Status"), Currency(), N("currency_rate", "Currency rate"), T("line_amount_type", "Line amount type"),
                N("subtotal", "Subtotal"), N("total_tax", "Total tax"), N("total", "Total"), N("amount_due", "Amount due"),
                N("amount_paid", "Amount paid"), N("amount_credited", "Amount credited"), D("fully_paid_date", "Fully paid"),
                F("sent_to_contact", "Sent to contact"), F("has_attachments", "Has attachments"), D("updated_at", "Updated"),
                N("days_to_due", "Days to due"), N("days_overdue", "Days overdue"), F("is_overdue", "Overdue"),
                N("outstanding_ratio", "Outstanding ratio"), T("document_direction", "Direction")
            ]),

        Source(InvoiceLines, "Invoice lines",
            "The line items of every invoice and bill (`Invoices[].LineItems[]`), with the invoice's own type, date, contact and status repeated so a line can be analysed alone.", "invoice_date",
            [
                Invoice(), T("invoice_number", "Invoice number"), T("invoice_type", "Invoice type"), D("invoice_date", "Invoice date"),
                Contact(), T("status", "Status"), Currency(), .. Line(item: true, discount: true)
            ]),

        Source(Payments, "Payments",
            "Payments against invoices and credit notes (`GET /Payments`); `payment_type` is ACCRECPAYMENT, ACCPAYPAYMENT, ARCREDITPAYMENT, … and `amount` is in the document's currency, `bank_amount` in the bank account's.", "date",
            [
                T("payment_id", "Payment ID"), T("payment_type", "Payment type"), D("date", "Date"), Invoice(),
                T("invoice_number", "Invoice number"), T("credit_note_id", "Credit note ID"), T("credit_note_number", "Credit note number"),
                Contact(), T("contact_name", "Contact"), Account(), T("account_code", "Account code"), T("account_name", "Account name"),
                N("amount", "Amount"), N("bank_amount", "Bank amount"), N("currency_rate", "Currency rate"), T("reference", "Reference"),
                T("status", "Status"), F("is_reconciled", "Reconciled"), T("batch_payment_id", "Batch payment ID"), D("updated_at", "Updated")
            ]),

        Source(BankTransactions, "Bank transactions",
            "Spend Money and Receive Money transactions (`GET /BankTransactions`); `type` is RECEIVE / SPEND or their prepayment and overpayment variants.", "date",
            [
                T("bank_transaction_id", "Bank transaction ID"), T("type", "Type"), Contact(), T("contact_name", "Contact"),
                Account("bank_account_id", "Bank account ID"), T("bank_account_code", "Bank account code"), T("bank_account_name", "Bank account"),
                D("date", "Date"), T("reference", "Reference"), T("status", "Status"), Currency(), N("currency_rate", "Currency rate"),
                N("subtotal", "Subtotal"), N("total_tax", "Total tax"), N("total", "Total"), F("is_reconciled", "Reconciled"),
                F("has_attachments", "Has attachments"), D("updated_at", "Updated")
            ]),

        Source(BankTransactionLines, "Bank transaction lines",
            "The line items of every bank transaction (`BankTransactions[].LineItems[]`), with the transaction's type, date, contact, bank account and status repeated.", "date",
            [
                T("bank_transaction_id", "Bank transaction ID"), T("type", "Type"), D("date", "Date"), Contact(),
                Account("bank_account_id", "Bank account ID"), T("status", "Status"), Currency(), .. Line(item: true, discount: false)
            ]),

        Source(BankTransfers, "Bank transfers",
            "Transfers between the organisation's own bank accounts (`GET /BankTransfers`), with the bank transaction created on each side.", "date",
            [
                T("bank_transfer_id", "Bank transfer ID"), D("date", "Date"),
                Account("from_account_id", "From account ID"), T("from_account_code", "From account code"), T("from_account_name", "From account"),
                Account("to_account_id", "To account ID"), T("to_account_code", "To account code"), T("to_account_name", "To account"),
                N("amount", "Amount"), N("currency_rate", "Currency rate"),
                T("from_bank_transaction_id", "From bank transaction ID"), T("to_bank_transaction_id", "To bank transaction ID"),
                F("from_is_reconciled", "From side reconciled"), F("to_is_reconciled", "To side reconciled"),
                T("reference", "Reference"), T("status", "Status"), D("created_at", "Created")
            ]),

        Source(CreditNotes, "Credit notes",
            "Credit notes (`GET /CreditNotes`); `type` is ACCRECCREDIT (to a customer) or ACCPAYCREDIT (from a supplier).", "date",
            [
                T("credit_note_id", "Credit note ID"), T("credit_note_number", "Credit note number"), T("type", "Type"), Contact(),
                T("contact_name", "Contact"), D("date", "Date"), T("status", "Status"), Currency(), N("currency_rate", "Currency rate"),
                N("subtotal", "Subtotal"), N("total_tax", "Total tax"), N("total", "Total"), N("remaining_credit", "Remaining credit"),
                N("applied_amount", "Applied amount"), D("fully_paid_date", "Fully paid"), T("reference", "Reference"),
                F("has_attachments", "Has attachments"), D("updated_at", "Updated")
            ]),

        Source(CreditNoteLines, "Credit note lines",
            "The line items of every credit note (`CreditNotes[].LineItems[]`), with the credit note's number, type, date, contact and status repeated.", "date",
            [
                T("credit_note_id", "Credit note ID"), T("credit_note_number", "Credit note number"), T("type", "Type"), D("date", "Date"),
                Contact(), T("status", "Status"), Currency(), .. Line(item: true, discount: true)
            ]),

        Source(ManualJournals, "Manual journals",
            "Manual journals (`GET /ManualJournals`). These are only the journals entered by hand — not the general ledger, which Xero's `/Journals` holds and which needs Advanced developer access; financial statements come from the report sources.", "date",
            [
                T("manual_journal_id", "Manual journal ID"), D("date", "Date"), T("status", "Status"), T("narration", "Narration"),
                T("line_amount_types", "Line amount types"), F("show_on_cash_basis_reports", "Shown on cash basis reports"),
                T("url", "URL"), F("has_attachments", "Has attachments"), D("updated_at", "Updated")
            ]),

        Source(ManualJournalLines, "Manual journal lines",
            "The lines of every manual journal (`ManualJournals[].JournalLines[]`); a positive `line_amount` is a debit, a negative one a credit. Xero gives a line no id of its own, so `journal_line_id` is the journal id and the line's position.", "date",
            [
                T("manual_journal_id", "Manual journal ID"), D("date", "Date"), T("status", "Status"), T("journal_line_id", "Journal line ID"),
                Account(), T("account_code", "Account code"), T("account_name", "Account name"), T("description", "Description"),
                N("line_amount", "Line amount"), T("tax_type", "Tax type"), N("tax_amount", "Tax amount"), .. Tracking(1), .. Tracking(2)
            ]),

        Source(TrackingCategories, "Tracking categories",
            "Tracking categories (`GET /TrackingCategories`, archived included) — usually department, branch, location or project. Xero allows two active ones.", null,
            [
                new("tracking_category_id", XeroFieldType.Text, "Tracking category ID", TrackingCategories, "tracking_category_id", "name"),
                T("name", "Name"), T("status", "Status")
            ]),

        Source(TrackingOptions, "Tracking options",
            "The options of every tracking category (`TrackingCategories[].Options[]`).", null,
            [
                new("tracking_category_id", XeroFieldType.Text, "Tracking category ID", TrackingCategories, "tracking_category_id", "name"),
                T("tracking_category_name", "Tracking category"),
                new("tracking_option_id", XeroFieldType.Text, "Tracking option ID", TrackingOptions, "tracking_option_id", "name"),
                T("name", "Name"), T("status", "Status")
            ]),

        Source(TaxRates, "Tax rates",
            "Tax rates (`GET /TaxRates`), with the display and effective rate and which account classes a rate may be used on.", null,
            [
                T("tax_type", "Tax type"), T("name", "Name"), T("status", "Status"), T("report_tax_type", "Report tax type"),
                N("display_tax_rate", "Display rate, %"), N("effective_rate", "Effective rate, %"),
                F("can_apply_to_assets", "Assets"), F("can_apply_to_equity", "Equity"), F("can_apply_to_expenses", "Expenses"),
                F("can_apply_to_liabilities", "Liabilities"), F("can_apply_to_revenue", "Revenue")
            ]),

        Source(Currencies, "Currencies",
            "The currencies enabled for the organisation (`GET /Currencies`).", null,
            [Currency("code", "Code"), T("description", "Description")]),

        Source(ProfitAndLoss, "Profit and loss",
            "The P&L (`GET /Reports/ProfitAndLoss`) month by month: one row per report line and month, `period_start`/`period_end` the calendar month. The filter's `period_start` bounds choose the months (none: the current financial year to date). `row_type` is `account`, `total` (Total Income, …) or `calculated` (Gross Profit, Net Profit) — sum only `account` rows. Naming `tracking_option_1_id` / `tracking_option_2_id` (= or IN) narrows the report to those options, one series per option.", "period_start",
            [
                D("period_start", "Period start"), D("period_end", "Period end"), T("period_label", "Period label"),
                .. ReportLine(), .. Tracking(1), .. Tracking(2), D("report_generated_at", "Report generated")
            ]),

        Source(BalanceSheet, "Balance sheet",
            "The balance sheet (`GET /Reports/BalanceSheet`) at every month end inside the filter's `as_of_date` bounds, plus the upper bound itself when it is not a month end (none: each month end of the current financial year, and today). `row_type` is `account`, `total` or `calculated`.", "as_of_date",
            [D("as_of_date", "As of"), T("period_label", "Period label"), .. ReportLine(), D("report_generated_at", "Report generated")]),

        Source(TrialBalance, "Trial balance",
            "The trial balance (`GET /Reports/TrialBalance`) on one date: the filter's `report_date` (its upper bound for a range; none: today). `debit`/`credit` and `ytd_debit`/`ytd_credit` are Xero's own two column pairs; `row_type` `total` is the totals line.", "report_date",
            [
                D("report_date", "Report date"), T("section", "Section"), T("row_type", "Row type"), T("label", "Label"),
                Account(), T("account_code", "Account code"), T("account_name", "Account name"),
                N("debit", "Debit"), N("credit", "Credit"), N("ytd_debit", "YTD debit"), N("ytd_credit", "YTD credit"),
                D("report_generated_at", "Report generated")
            ]),

        Source(BankSummary, "Bank summary",
            "Opening balance, cash received, cash spent and closing balance per bank account (`GET /Reports/BankSummary`), month by month inside the filter's `period_start` bounds (none: the current financial year to date). `row_type` `total` is the all-accounts line.", "period_start",
            [
                D("period_start", "Period start"), D("period_end", "Period end"), T("row_type", "Row type"),
                Account(), T("bank_account_name", "Bank account"), N("opening_balance", "Opening balance"),
                N("cash_received", "Cash received"), N("cash_spent", "Cash spent"), N("closing_balance", "Closing balance"),
                D("report_generated_at", "Report generated")
            ]),

        Source(AgedReceivablesByContact, "Aged receivables by contact",
            "Xero's aged receivables for the contacts the filter names: `contact_id` = or IN is required, one report call per contact. `report_date` (upper bound) is the as-at date, `invoice_date` bounds go to the report's from/to. `row_type` is `opening_balance`, `invoice`, `total` or `closing_balance`; `days_overdue` and `ageing_bucket` are derived from the due date.", "report_date",
            Aged("receivable")),

        Source(AgedPayablesByContact, "Aged payables by contact",
            "Xero's aged payables for the contacts the filter names: `contact_id` = or IN is required, one report call per contact. Otherwise as aged receivables.", "report_date",
            Aged("payable")),

        Source(Budgets, "Budgets",
            "Budgets (`GET /Budgets`): the overall budget and the tracking-category budgets, with the tracking option a budget is for.", null,
            [Budget(), T("type", "Type"), T("description", "Description"), D("updated_at", "Updated"), .. Tracking(1), .. Tracking(2)]),

        Source(BudgetLines, "Budget lines",
            "Budgeted amount per budget, account and month (`GET /Budgets/{id}` balances). The filter's `period` bounds choose the months (none: the current financial year).", "period",
            [
                Budget(), T("budget_type", "Budget type"), T("budget_description", "Budget"), Account(), T("account_code", "Account code"),
                T("account_name", "Account name"), D("period", "Period"), N("amount", "Amount"), N("unit_amount", "Unit amount"), T("notes", "Notes")
            ]),

        Source(BudgetSummary, "Budget summary",
            "Xero's budget summary report for the overall budget (`GET /Reports/BudgetSummary`) month by month inside the filter's `period_start` bounds (none: the current financial year). `row_type` is `account`, `total` or `calculated`.", "period_start",
            [
                D("period_start", "Period start"), D("period_end", "Period end"), T("period_label", "Period label"),
                .. ReportLine(), D("report_generated_at", "Report generated")
            ])
    ];

    public static readonly IReadOnlyDictionary<string, XeroSource> ById =
        All.ToDictionary(static source => source.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A row carrying exactly the source's fields — a missing one reads null, and nothing a loader kept for its own
    /// joins leaks out.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> Project(XeroSource source, IReadOnlyDictionary<string, object?> row)
    {
        var projected = new Dictionary<string, object?>(source.Fields.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var field in source.Fields)
        {
            projected[field.Name] = row.TryGetValue(field.Name, out var value) ? value : null;
        }

        return projected;
    }
}
