using Intera.Xero.Api.Infrastructure;
using Intera.Xero.Api.Models;
using System.Text.Json;

namespace Intera.Xero.Api.Services;

using Row = Dictionary<string, object?>;

/// <summary>
/// The transaction sources. Headers and lines of one kind are the same read — a paged list answer carries the line
/// items — so <c>invoices</c> and <c>invoice_lines</c> asked for together cost Xero one set of calls, not two.
/// </summary>
/// <remarks>
/// The filter's bounds on the source's date field go to Xero as a <c>where</c> on <c>Date</c>; invoices also pass
/// the contacts, statuses, ids and type the filter names. Everything else is filtered in memory over what comes back,
/// so a condition Xero cannot take costs calls, never rows.
/// </remarks>
public sealed partial class XeroDataService
{
    private static readonly HashSet<string> InvoiceStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "DRAFT", "SUBMITTED", "AUTHORISED", "PAID", "VOIDED", "DELETED"
    };

    private async Task<List<Row>> LoadInvoicesAsync(ReadContext context, bool lines, CancellationToken cancellationToken)
    {
        var (from, to) = Window(context.Query, context.Source.DateField);
        var query = new Dictionary<string, string?> { ["unitdp"] = "4" };

        var types = Keys(context.Query, "invoice_type");
        var typeWhere = types.Count == 1 && types[0].ToUpperInvariant() is "ACCREC" or "ACCPAY"
            ? $"Type==\"{types[0].ToUpperInvariant()}\""
            : null;
        query["where"] = And(DateWhere("Date", from, to), typeWhere);

        if (GuidKeys(context.Query, "contact_id") is { } contacts)
        {
            query["ContactIDs"] = string.Join(',', contacts);
        }

        if (GuidKeys(context.Query, "invoice_id") is { } ids)
        {
            query["IDs"] = string.Join(',', ids);
        }

        var statuses = Keys(context.Query, "status");
        if (statuses.Count > 0 && statuses.All(InvoiceStatuses.Contains))
        {
            query["Statuses"] = string.Join(',', statuses.Select(static status => status.ToUpperInvariant()));
        }

        var invoices = await client.GetCollectionAsync(context.Tenant, "Invoices", "Invoices", query, paged: true, reference: false, cancellationToken)
            .ConfigureAwait(false);

        if (!lines)
        {
            return [.. invoices.Items.Select(invoice => InvoiceRow(context, invoice, invoices.FetchedAt))];
        }

        var accounts = await ReadAccountCodeIndexAsync(context, cancellationToken).ConfigureAwait(false);
        var rows = new List<Row>();

        foreach (var invoice in invoices.Items)
        {
            var contact = XeroJson.Object(invoice, "Contact");
            var header = new Row
            {
                ["invoice_id"] = XeroJson.String(invoice, "InvoiceID"),
                ["invoice_number"] = XeroJson.String(invoice, "InvoiceNumber"),
                ["invoice_type"] = XeroJson.String(invoice, "Type"),
                ["invoice_date"] = XeroJson.Date(invoice, "Date"),
                ["contact_id"] = XeroJson.String(contact, "ContactID"),
                ["status"] = XeroJson.String(invoice, "Status"),
                ["currency_code"] = XeroJson.String(invoice, "CurrencyCode")
            };

            AddLines(context, rows, header, XeroJson.Array(invoice, "LineItems"), accounts, XeroJson.Timestamp(invoice, "UpdatedDateUTC"), invoices.FetchedAt);
        }

        return rows;
    }

    private static Row InvoiceRow(ReadContext context, JsonElement invoice, DateTime fetchedAt)
    {
        var contact = XeroJson.Object(invoice, "Contact");
        var type = XeroJson.String(invoice, "Type");
        var status = XeroJson.String(invoice, "Status");
        var dueDate = XeroJson.Date(invoice, "DueDate");
        var total = XeroJson.Decimal(invoice, "Total");
        var amountDue = XeroJson.Decimal(invoice, "AmountDue");

        // Derived against today. Only an approved invoice with money still owed can be overdue: a draft or one
        // awaiting approval has not been issued, and a paid one is past its due date without being late.
        int? daysToDue = dueDate is { } due ? DateOnly.FromDateTime(due).DayNumber - context.Today.DayNumber : null;
        var isOverdue = string.Equals(status, "AUTHORISED", StringComparison.OrdinalIgnoreCase)
            && amountDue > 0
            && daysToDue < 0;

        return Stamp(context, new Row
        {
            ["invoice_id"] = XeroJson.String(invoice, "InvoiceID"),
            ["invoice_type"] = type,
            ["invoice_number"] = XeroJson.String(invoice, "InvoiceNumber"),
            ["reference"] = XeroJson.String(invoice, "Reference"),
            ["contact_id"] = XeroJson.String(contact, "ContactID"),
            ["contact_name"] = XeroJson.String(contact, "Name"),
            ["invoice_date"] = XeroJson.Date(invoice, "Date"),
            ["due_date"] = dueDate,
            ["status"] = status,
            ["currency_code"] = XeroJson.String(invoice, "CurrencyCode"),
            ["currency_rate"] = XeroJson.Decimal(invoice, "CurrencyRate"),
            ["line_amount_type"] = XeroJson.String(invoice, "LineAmountTypes"),
            ["subtotal"] = XeroJson.Decimal(invoice, "SubTotal"),
            ["total_tax"] = XeroJson.Decimal(invoice, "TotalTax"),
            ["total"] = total,
            ["amount_due"] = amountDue,
            ["amount_paid"] = XeroJson.Decimal(invoice, "AmountPaid"),
            ["amount_credited"] = XeroJson.Decimal(invoice, "AmountCredited"),
            ["fully_paid_date"] = XeroJson.Date(invoice, "FullyPaidOnDate"),
            ["sent_to_contact"] = XeroJson.Boolean(invoice, "SentToContact"),
            ["has_attachments"] = XeroJson.Boolean(invoice, "HasAttachments"),
            ["updated_at"] = XeroJson.Timestamp(invoice, "UpdatedDateUTC"),
            ["days_to_due"] = daysToDue,
            ["days_overdue"] = isOverdue ? -daysToDue : 0,
            ["is_overdue"] = isOverdue,
            ["outstanding_ratio"] = total is { } t && t != 0 && amountDue is { } d ? Math.Round(d / t, 6) : null,
            ["document_direction"] = type?.ToUpperInvariant() switch
            {
                "ACCREC" => "customer",
                "ACCPAY" => "supplier",
                _ => null
            }
        }, XeroJson.Timestamp(invoice, "UpdatedDateUTC"), fetchedAt);
    }

    private async Task<List<Row>> LoadPaymentsAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var (from, to) = Window(context.Query, context.Source.DateField);
        var payments = await client.GetCollectionAsync(
            context.Tenant,
            "Payments",
            "Payments",
            new Dictionary<string, string?> { ["where"] = DateWhere("Date", from, to) },
            paged: true,
            reference: false,
            cancellationToken).ConfigureAwait(false);

        var accounts = await ReadAccountIndexAsync(context, cancellationToken).ConfigureAwait(false);

        return
        [
            .. payments.Items.Select(payment =>
            {
                var invoice = XeroJson.Object(payment, "Invoice");
                var creditNote = XeroJson.Object(payment, "CreditNote");
                var contact = (invoice ?? creditNote) is { } document ? XeroJson.Object(document, "Contact") : null;
                var account = XeroJson.Object(payment, "Account");
                var accountId = XeroJson.String(account, "AccountID");
                var known = accountId is not null && accounts.TryGetValue(accountId, out var entry) ? entry : default;

                return Stamp(context, new Row
                {
                    ["payment_id"] = XeroJson.String(payment, "PaymentID"),
                    ["payment_type"] = XeroJson.String(payment, "PaymentType"),
                    ["date"] = XeroJson.Date(payment, "Date"),
                    ["invoice_id"] = XeroJson.String(invoice, "InvoiceID"),
                    ["invoice_number"] = XeroJson.String(invoice, "InvoiceNumber"),
                    ["credit_note_id"] = XeroJson.String(creditNote, "CreditNoteID"),
                    ["credit_note_number"] = XeroJson.String(creditNote, "CreditNoteNumber"),
                    ["contact_id"] = XeroJson.String(contact, "ContactID"),
                    ["contact_name"] = XeroJson.String(contact, "Name"),
                    ["account_id"] = accountId,
                    ["account_code"] = XeroJson.String(account, "Code") ?? known.Code,
                    ["account_name"] = XeroJson.String(account, "Name") ?? known.Name,
                    ["amount"] = XeroJson.Decimal(payment, "Amount"),
                    ["bank_amount"] = XeroJson.Decimal(payment, "BankAmount"),
                    ["currency_rate"] = XeroJson.Decimal(payment, "CurrencyRate"),
                    ["reference"] = XeroJson.String(payment, "Reference"),
                    ["status"] = XeroJson.String(payment, "Status"),
                    ["is_reconciled"] = XeroJson.Boolean(payment, "IsReconciled"),
                    ["batch_payment_id"] = XeroJson.String(payment, "BatchPaymentID"),
                    ["updated_at"] = XeroJson.Timestamp(payment, "UpdatedDateUTC")
                }, XeroJson.Timestamp(payment, "UpdatedDateUTC"), payments.FetchedAt);
            })
        ];
    }

    private async Task<List<Row>> LoadBankTransactionsAsync(ReadContext context, bool lines, CancellationToken cancellationToken)
    {
        var (from, to) = Window(context.Query, context.Source.DateField);
        var transactions = await client.GetCollectionAsync(
            context.Tenant,
            "BankTransactions",
            "BankTransactions",
            new Dictionary<string, string?> { ["unitdp"] = "4", ["where"] = DateWhere("Date", from, to) },
            paged: true,
            reference: false,
            cancellationToken).ConfigureAwait(false);

        if (!lines)
        {
            return
            [
                .. transactions.Items.Select(transaction =>
                {
                    var contact = XeroJson.Object(transaction, "Contact");
                    var bank = XeroJson.Object(transaction, "BankAccount");

                    return Stamp(context, new Row
                    {
                        ["bank_transaction_id"] = XeroJson.String(transaction, "BankTransactionID"),
                        ["type"] = XeroJson.String(transaction, "Type"),
                        ["contact_id"] = XeroJson.String(contact, "ContactID"),
                        ["contact_name"] = XeroJson.String(contact, "Name"),
                        ["bank_account_id"] = XeroJson.String(bank, "AccountID"),
                        ["bank_account_code"] = XeroJson.String(bank, "Code"),
                        ["bank_account_name"] = XeroJson.String(bank, "Name"),
                        ["date"] = XeroJson.Date(transaction, "Date"),
                        ["reference"] = XeroJson.String(transaction, "Reference"),
                        ["status"] = XeroJson.String(transaction, "Status"),
                        ["currency_code"] = XeroJson.String(transaction, "CurrencyCode"),
                        ["currency_rate"] = XeroJson.Decimal(transaction, "CurrencyRate"),
                        ["subtotal"] = XeroJson.Decimal(transaction, "SubTotal"),
                        ["total_tax"] = XeroJson.Decimal(transaction, "TotalTax"),
                        ["total"] = XeroJson.Decimal(transaction, "Total"),
                        ["is_reconciled"] = XeroJson.Boolean(transaction, "IsReconciled"),
                        ["has_attachments"] = XeroJson.Boolean(transaction, "HasAttachments"),
                        ["updated_at"] = XeroJson.Timestamp(transaction, "UpdatedDateUTC")
                    }, XeroJson.Timestamp(transaction, "UpdatedDateUTC"), transactions.FetchedAt);
                })
            ];
        }

        var accounts = await ReadAccountCodeIndexAsync(context, cancellationToken).ConfigureAwait(false);
        var rows = new List<Row>();

        foreach (var transaction in transactions.Items)
        {
            var header = new Row
            {
                ["bank_transaction_id"] = XeroJson.String(transaction, "BankTransactionID"),
                ["type"] = XeroJson.String(transaction, "Type"),
                ["date"] = XeroJson.Date(transaction, "Date"),
                ["contact_id"] = XeroJson.String(XeroJson.Object(transaction, "Contact"), "ContactID"),
                ["bank_account_id"] = XeroJson.String(XeroJson.Object(transaction, "BankAccount"), "AccountID"),
                ["status"] = XeroJson.String(transaction, "Status"),
                ["currency_code"] = XeroJson.String(transaction, "CurrencyCode")
            };

            AddLines(context, rows, header, XeroJson.Array(transaction, "LineItems"), accounts, XeroJson.Timestamp(transaction, "UpdatedDateUTC"), transactions.FetchedAt);
        }

        return rows;
    }

    private async Task<List<Row>> LoadBankTransfersAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var (from, to) = Window(context.Query, context.Source.DateField);
        var transfers = await client.GetCollectionAsync(
            context.Tenant,
            "BankTransfers",
            "BankTransfers",
            new Dictionary<string, string?> { ["where"] = DateWhere("Date", from, to) },
            paged: false,
            reference: false,
            cancellationToken).ConfigureAwait(false);

        return
        [
            .. transfers.Items.Select(transfer =>
            {
                var source = XeroJson.Object(transfer, "FromBankAccount");
                var target = XeroJson.Object(transfer, "ToBankAccount");

                return Stamp(context, new Row
                {
                    ["bank_transfer_id"] = XeroJson.String(transfer, "BankTransferID"),
                    ["date"] = XeroJson.Date(transfer, "Date"),
                    ["from_account_id"] = XeroJson.String(source, "AccountID"),
                    ["from_account_code"] = XeroJson.String(source, "Code"),
                    ["from_account_name"] = XeroJson.String(source, "Name"),
                    ["to_account_id"] = XeroJson.String(target, "AccountID"),
                    ["to_account_code"] = XeroJson.String(target, "Code"),
                    ["to_account_name"] = XeroJson.String(target, "Name"),
                    ["amount"] = XeroJson.Decimal(transfer, "Amount"),
                    ["currency_rate"] = XeroJson.Decimal(transfer, "CurrencyRate"),
                    ["from_bank_transaction_id"] = XeroJson.String(transfer, "FromBankTransactionID"),
                    ["to_bank_transaction_id"] = XeroJson.String(transfer, "ToBankTransactionID"),
                    ["from_is_reconciled"] = XeroJson.Boolean(transfer, "FromIsReconciled"),
                    ["to_is_reconciled"] = XeroJson.Boolean(transfer, "ToIsReconciled"),
                    ["reference"] = XeroJson.String(transfer, "Reference"),
                    ["status"] = XeroJson.String(transfer, "Status"),
                    ["created_at"] = XeroJson.Timestamp(transfer, "CreatedDateUTC")
                }, XeroJson.Timestamp(transfer, "CreatedDateUTC"), transfers.FetchedAt);
            })
        ];
    }

    private async Task<List<Row>> LoadCreditNotesAsync(ReadContext context, bool lines, CancellationToken cancellationToken)
    {
        var (from, to) = Window(context.Query, context.Source.DateField);
        var notes = await client.GetCollectionAsync(
            context.Tenant,
            "CreditNotes",
            "CreditNotes",
            new Dictionary<string, string?> { ["unitdp"] = "4", ["where"] = DateWhere("Date", from, to) },
            paged: true,
            reference: false,
            cancellationToken).ConfigureAwait(false);

        if (!lines)
        {
            return
            [
                .. notes.Items.Select(note =>
                {
                    var contact = XeroJson.Object(note, "Contact");

                    return Stamp(context, new Row
                    {
                        ["credit_note_id"] = XeroJson.String(note, "CreditNoteID"),
                        ["credit_note_number"] = XeroJson.String(note, "CreditNoteNumber"),
                        ["type"] = XeroJson.String(note, "Type"),
                        ["contact_id"] = XeroJson.String(contact, "ContactID"),
                        ["contact_name"] = XeroJson.String(contact, "Name"),
                        ["date"] = XeroJson.Date(note, "Date"),
                        ["status"] = XeroJson.String(note, "Status"),
                        ["currency_code"] = XeroJson.String(note, "CurrencyCode"),
                        ["currency_rate"] = XeroJson.Decimal(note, "CurrencyRate"),
                        ["subtotal"] = XeroJson.Decimal(note, "SubTotal"),
                        ["total_tax"] = XeroJson.Decimal(note, "TotalTax"),
                        ["total"] = XeroJson.Decimal(note, "Total"),
                        ["remaining_credit"] = XeroJson.Decimal(note, "RemainingCredit"),
                        ["applied_amount"] = XeroJson.Decimal(note, "AppliedAmount"),
                        ["fully_paid_date"] = XeroJson.Date(note, "FullyPaidOnDate"),
                        ["reference"] = XeroJson.String(note, "Reference"),
                        ["has_attachments"] = XeroJson.Boolean(note, "HasAttachments"),
                        ["updated_at"] = XeroJson.Timestamp(note, "UpdatedDateUTC")
                    }, XeroJson.Timestamp(note, "UpdatedDateUTC"), notes.FetchedAt);
                })
            ];
        }

        var accounts = await ReadAccountCodeIndexAsync(context, cancellationToken).ConfigureAwait(false);
        var rows = new List<Row>();

        foreach (var note in notes.Items)
        {
            var header = new Row
            {
                ["credit_note_id"] = XeroJson.String(note, "CreditNoteID"),
                ["credit_note_number"] = XeroJson.String(note, "CreditNoteNumber"),
                ["type"] = XeroJson.String(note, "Type"),
                ["date"] = XeroJson.Date(note, "Date"),
                ["contact_id"] = XeroJson.String(XeroJson.Object(note, "Contact"), "ContactID"),
                ["status"] = XeroJson.String(note, "Status"),
                ["currency_code"] = XeroJson.String(note, "CurrencyCode")
            };

            AddLines(context, rows, header, XeroJson.Array(note, "LineItems"), accounts, XeroJson.Timestamp(note, "UpdatedDateUTC"), notes.FetchedAt);
        }

        return rows;
    }

    private async Task<List<Row>> LoadManualJournalsAsync(ReadContext context, bool lines, CancellationToken cancellationToken)
    {
        var (from, to) = Window(context.Query, context.Source.DateField);
        var journals = await client.GetCollectionAsync(
            context.Tenant,
            "ManualJournals",
            "ManualJournals",
            new Dictionary<string, string?> { ["where"] = DateWhere("Date", from, to) },
            paged: true,
            reference: false,
            cancellationToken).ConfigureAwait(false);

        if (!lines)
        {
            return
            [
                .. journals.Items.Select(journal => Stamp(context, new Row
                {
                    ["manual_journal_id"] = XeroJson.String(journal, "ManualJournalID"),
                    ["date"] = XeroJson.Date(journal, "Date"),
                    ["status"] = XeroJson.String(journal, "Status"),
                    ["narration"] = XeroJson.String(journal, "Narration"),
                    ["line_amount_types"] = XeroJson.String(journal, "LineAmountTypes"),
                    ["show_on_cash_basis_reports"] = XeroJson.Boolean(journal, "ShowOnCashBasisReports"),
                    ["url"] = XeroJson.String(journal, "Url"),
                    ["has_attachments"] = XeroJson.Boolean(journal, "HasAttachments"),
                    ["updated_at"] = XeroJson.Timestamp(journal, "UpdatedDateUTC")
                }, XeroJson.Timestamp(journal, "UpdatedDateUTC"), journals.FetchedAt))
            ];
        }

        var accounts = await ReadAccountCodeIndexAsync(context, cancellationToken).ConfigureAwait(false);
        var rows = new List<Row>();

        foreach (var journal in journals.Items)
        {
            var journalId = XeroJson.String(journal, "ManualJournalID");
            var position = 0;

            foreach (var line in XeroJson.Array(journal, "JournalLines"))
            {
                position++;
                var account = accounts.Resolve(XeroJson.String(line, "AccountID"), XeroJson.String(line, "AccountCode"));
                var row = new Row
                {
                    ["manual_journal_id"] = journalId,
                    ["date"] = XeroJson.Date(journal, "Date"),
                    ["status"] = XeroJson.String(journal, "Status"),
                    ["journal_line_id"] = $"{journalId}:{position}",
                    ["account_id"] = account.Id,
                    ["account_code"] = account.Code,
                    ["account_name"] = account.Name,
                    ["description"] = XeroJson.String(line, "Description"),
                    ["line_amount"] = XeroJson.Decimal(line, "LineAmount"),
                    ["tax_type"] = XeroJson.String(line, "TaxType"),
                    ["tax_amount"] = XeroJson.Decimal(line, "TaxAmount")
                };

                AddTracking(row, XeroJson.Array(line, "Tracking"));
                rows.Add(Stamp(context, row, XeroJson.Timestamp(journal, "UpdatedDateUTC"), journals.FetchedAt));
            }
        }

        return rows;
    }

    private async Task<List<Row>> LoadBudgetsAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var budgets = await client.GetCollectionAsync(context.Tenant, "Budgets", "Budgets", null, paged: false, reference: false, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. budgets.Items.Select(budget =>
            {
                var row = new Row
                {
                    ["budget_id"] = XeroJson.String(budget, "BudgetID"),
                    ["type"] = XeroJson.String(budget, "Type"),
                    ["description"] = XeroJson.String(budget, "Description"),
                    ["updated_at"] = XeroJson.Timestamp(budget, "UpdatedDateUTC")
                };

                AddTracking(row, XeroJson.Array(budget, "Tracking"));
                return Stamp(context, row, XeroJson.Timestamp(budget, "UpdatedDateUTC"), budgets.FetchedAt);
            })
        ];
    }

    private async Task<List<Row>> LoadBudgetLinesAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var (from, to) = Window(context.Query, context.Source.DateField);
        var yearStart = FinancialYearStart(context, context.Today);
        var dateFrom = from ?? (to is { } upper ? FinancialYearStart(context, upper) : yearStart);
        var dateTo = to ?? dateFrom.AddYears(1).AddDays(-1);

        var budgets = await client.GetCollectionAsync(context.Tenant, "Budgets", "Budgets", null, paged: false, reference: false, cancellationToken)
            .ConfigureAwait(false);

        var wanted = GuidKeys(context.Query, "budget_id");
        var selected = budgets.Items
            .Where(budget => wanted is null || wanted.Contains(XeroJson.String(budget, "BudgetID") ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (selected.Count > Options.MaxBudgets)
        {
            throw new XeroRequestException(
                $"The organisation has {selected.Count} budgets and each is one call (Xero:MaxBudgets is {Options.MaxBudgets}). Name the budgets in the filter (budget_id = / IN).");
        }

        var accounts = await ReadAccountCodeIndexAsync(context, cancellationToken).ConfigureAwait(false);
        var rows = new List<Row>();

        foreach (var budget in selected)
        {
            var budgetId = XeroJson.String(budget, "BudgetID");
            if (budgetId is null)
            {
                continue;
            }

            var detail = await client.GetAsync(
                context.Tenant,
                $"Budgets/{budgetId}",
                new Dictionary<string, string?> { ["DateFrom"] = IsoDate(dateFrom), ["DateTo"] = IsoDate(dateTo) },
                reference: false,
                cancellationToken).ConfigureAwait(false);

            if (XeroJson.First(detail.Root, "Budgets") is not { } full)
            {
                continue;
            }

            foreach (var line in XeroJson.Array(full, "BudgetLines"))
            {
                var account = accounts.Resolve(XeroJson.String(line, "AccountID"), XeroJson.String(line, "AccountCode"));

                foreach (var balance in XeroJson.Array(line, "BudgetBalances"))
                {
                    rows.Add(Stamp(context, new Row
                    {
                        ["budget_id"] = budgetId,
                        ["budget_type"] = XeroJson.String(full, "Type"),
                        ["budget_description"] = XeroJson.String(full, "Description"),
                        ["account_id"] = account.Id,
                        ["account_code"] = account.Code,
                        ["account_name"] = account.Name,
                        ["period"] = XeroJson.Date(balance, "Period"),
                        ["amount"] = XeroJson.Decimal(balance, "Amount"),
                        ["unit_amount"] = XeroJson.Decimal(balance, "UnitAmount"),
                        ["notes"] = XeroJson.String(balance, "Notes")
                    }, XeroJson.Timestamp(full, "UpdatedDateUTC"), detail.FetchedAt));
                }
            }
        }

        return rows;
    }

    // ---- Lines ----------------------------------------------------------------------------------------------

    /// <summary>One row per line item, each starting from the document fields in <paramref name="header"/>.</summary>
    private static void AddLines(
        ReadContext context,
        List<Row> rows,
        Row header,
        IEnumerable<JsonElement> lines,
        AccountCodeIndex accounts,
        DateTime? updatedAt,
        DateTime fetchedAt)
    {
        foreach (var line in lines)
        {
            var account = accounts.Resolve(XeroJson.String(line, "AccountID"), XeroJson.String(line, "AccountCode"));
            var row = new Row(header, StringComparer.OrdinalIgnoreCase)
            {
                ["line_item_id"] = XeroJson.String(line, "LineItemID"),
                ["description"] = XeroJson.String(line, "Description"),
                ["quantity"] = XeroJson.Decimal(line, "Quantity"),
                ["unit_amount"] = XeroJson.Decimal(line, "UnitAmount"),
                ["line_amount"] = XeroJson.Decimal(line, "LineAmount"),
                ["discount_rate"] = XeroJson.Decimal(line, "DiscountRate"),
                ["discount_amount"] = XeroJson.Decimal(line, "DiscountAmount"),
                ["account_id"] = account.Id,
                ["account_code"] = account.Code,
                ["item_code"] = XeroJson.String(line, "ItemCode"),
                ["tax_type"] = XeroJson.String(line, "TaxType"),
                ["tax_amount"] = XeroJson.Decimal(line, "TaxAmount")
            };

            AddTracking(row, XeroJson.Array(line, "Tracking"));
            rows.Add(Stamp(context, row, updatedAt, fetchedAt));
        }
    }

    /// <summary>The first two tracking entries as slots 1 and 2 — Xero allows two active categories.</summary>
    private static void AddTracking(Row row, IEnumerable<JsonElement> tracking)
    {
        var slot = 0;
        foreach (var entry in tracking)
        {
            if (++slot > 2)
            {
                break;
            }

            row[$"tracking_category_{slot}_id"] = XeroJson.String(entry, "TrackingCategoryID");
            row[$"tracking_category_{slot}"] = XeroJson.String(entry, "Name");
            row[$"tracking_option_{slot}_id"] = XeroJson.String(entry, "TrackingOptionID");
            row[$"tracking_option_{slot}"] = XeroJson.String(entry, "Option");
        }
    }

    /// <summary>
    /// Fills in whichever of a line's account id and code Xero left out. Lines always carry the code, the id only
    /// sometimes, and joins are meant to run on the id.
    /// </summary>
    private sealed class AccountCodeIndex(
        Dictionary<string, (string? Code, string? Name)> byId,
        Dictionary<string, string> idByCode)
    {
        public (string? Id, string? Code, string? Name) Resolve(string? id, string? code)
        {
            id ??= code is not null && idByCode.TryGetValue(code, out var found) ? found : null;
            var known = id is not null && byId.TryGetValue(id, out var entry) ? entry : default;
            return (id, code ?? known.Code, known.Name);
        }
    }

    private async Task<AccountCodeIndex> ReadAccountCodeIndexAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var byId = await ReadAccountIndexAsync(context, cancellationToken).ConfigureAwait(false);
        var idByCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (id, account) in byId)
        {
            if (account.Code is { } code)
            {
                idByCode.TryAdd(code, id);
            }
        }

        return new AccountCodeIndex(byId, idByCode);
    }
}
