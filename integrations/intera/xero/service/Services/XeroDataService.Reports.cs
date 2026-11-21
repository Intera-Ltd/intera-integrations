using Intera.Xero.Api.Infrastructure;
using System.Globalization;

namespace Intera.Xero.Api.Services;

using Row = Dictionary<string, object?>;

/// <summary>
/// The report sources (<c>/Reports/*</c>), flattened to one row per report line and period.
/// </summary>
/// <remarks>
/// <para>
/// <b>Monthly grain.</b> The P&amp;L, the balance sheet, the budget summary and the
/// bank summary are read month by month over the window the filter's date bounds give, so one query answers a time
/// series and the flows add up across months. Xero returns up to twelve monthly columns per call (<c>periods</c> /
/// <c>timeframe</c>); the bank summary has no such parameter and costs a call per month.
/// </para>
/// <para>
/// <b>Columns are matched by their header label, not by position.</b> The P&amp;L and the balance sheet list the
/// newest period first, the budget summary the oldest, and a P&amp;L comparison column is not always a calendar month
/// (see <see cref="ProfitAndLossMonth"/>). So each column's label is read as a date; only an unreadable label falls
/// back to the position, and a month no column answered is read again on its own — a surprise costs calls, not
/// numbers. Every call is counted against <c>Xero:MaxReportCalls</c>.
/// </para>
/// </remarks>
public sealed partial class XeroDataService
{
    private const int MaxPeriodsPerCall = 12;

    // ---- Profit and loss ------------------------------------------------------------------------------------

    private async Task<List<Row>> LoadProfitAndLossAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var (from, to) = MonthlyWindow(context, "period_start", "period_end", wholeYearByDefault: false);
        var months = Months(from, to);
        var selections = await TrackingSelectionsAsync(context, cancellationToken).ConfigureAwait(false);
        var accounts = await ReadAccountIndexAsync(context, cancellationToken).ConfigureAwait(false);
        var calls = new CallBudget(Options.MaxReportCalls);
        var rows = new List<Row>();

        foreach (var selection in selections)
        {
            var columns = await ReadSeriesAsync(
                context,
                "Reports/ProfitAndLoss",
                months,
                chunk => new Dictionary<string, string?>(selection.Parameters)
                {
                    ["fromDate"] = IsoDate(chunk[^1]),
                    ["toDate"] = IsoDate(MonthEnd(chunk[^1])),
                    ["periods"] = chunk.Count > 1 ? (chunk.Count - 1).ToString(CultureInfo.InvariantCulture) : null,
                    ["timeframe"] = chunk.Count > 1 ? "MONTH" : null
                },
                ProfitAndLossMonth,
                (chunk, index) => chunk[^1].AddMonths(-index),
                calls,
                cancellationToken,
                ProfitAndLossChunks).ConfigureAwait(false);

            foreach (var column in columns)
            {
                foreach (var line in column.Report.Lines)
                {
                    var row = ReportLineRow(line, XeroJson.ParseDecimal(line.Value(column.Column)), accounts);
                    row["period_start"] = ToDateTime(column.Key);
                    row["period_end"] = ToDateTime(MonthEnd(column.Key));
                    row["period_label"] = column.Label;
                    row["report_generated_at"] = column.Report.UpdatedAt;
                    selection.Apply(row);
                    rows.Add(Stamp(context, row, column.Report.UpdatedAt, column.FetchedAt));
                }
            }
        }

        return rows;
    }

    // ---- Balance sheet --------------------------------------------------------------------------------------

    private async Task<List<Row>> LoadBalanceSheetAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var (from, to) = Window(context.Query, "as_of_date");
        var upper = to ?? context.Today;
        var lower = from ?? FinancialYearStart(context, upper);
        if (lower > upper)
        {
            return [];
        }

        // Every month end inside the window, and the upper bound itself when it is not one (typically today).
        var monthEnds = Months(lower, upper).Select(MonthEnd).Where(date => date >= lower && date <= upper).ToList();
        if (monthEnds.Count + 1 > Options.MaxReportMonths)
        {
            throw TooManyMonths(monthEnds.Count + 1);
        }

        var accounts = await ReadAccountIndexAsync(context, cancellationToken).ConfigureAwait(false);
        var calls = new CallBudget(Options.MaxReportCalls);

        var columns = await ReadSeriesAsync(
            context,
            "Reports/BalanceSheet",
            monthEnds,
            chunk => new Dictionary<string, string?>
            {
                ["date"] = IsoDate(chunk[^1]),
                ["periods"] = chunk.Count > 1 ? (chunk.Count - 1).ToString(CultureInfo.InvariantCulture) : null,
                ["timeframe"] = chunk.Count > 1 ? "MONTH" : null
            },
            label => label.HasDay ? label.Date : MonthEnd(label.Date),
            (chunk, index) => MonthEnd(chunk[^1].AddMonths(-index)),
            calls,
            cancellationToken).ConfigureAwait(false);

        if (!monthEnds.Contains(upper))
        {
            columns.AddRange(await ReadSeriesAsync(
                context,
                "Reports/BalanceSheet",
                [upper],
                _ => new Dictionary<string, string?> { ["date"] = IsoDate(upper) },
                label => label.Date,
                (_, _) => upper,
                calls,
                cancellationToken).ConfigureAwait(false));
        }

        var rows = new List<Row>();
        foreach (var column in columns)
        {
            foreach (var line in column.Report.Lines)
            {
                var row = ReportLineRow(line, XeroJson.ParseDecimal(line.Value(column.Column)), accounts);
                row["as_of_date"] = ToDateTime(column.Key);
                row["period_label"] = column.Label;
                row["report_generated_at"] = column.Report.UpdatedAt;
                rows.Add(Stamp(context, row, column.Report.UpdatedAt, column.FetchedAt));
            }
        }

        return rows;
    }

    // ---- Budget summary -------------------------------------------------------------------------------------

    private async Task<List<Row>> LoadBudgetSummaryAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var (from, to) = MonthlyWindow(context, "period_start", "period_end", wholeYearByDefault: true);
        var months = Months(from, to);
        var accounts = await ReadAccountIndexAsync(context, cancellationToken).ConfigureAwait(false);
        var calls = new CallBudget(Options.MaxReportCalls);

        // Unlike the P&L, the budget summary starts at `date` and runs forward, oldest column first, and `timeframe`
        // is a number of months rather than a word.
        var columns = await ReadSeriesAsync(
            context,
            "Reports/BudgetSummary",
            months,
            chunk => new Dictionary<string, string?>
            {
                ["date"] = IsoDate(chunk[0]),
                ["periods"] = chunk.Count.ToString(CultureInfo.InvariantCulture),
                ["timeframe"] = "1"
            },
            label => FirstOfMonth(label.Date),
            (chunk, index) => chunk[0].AddMonths(index),
            calls,
            cancellationToken).ConfigureAwait(false);

        var rows = new List<Row>();
        foreach (var column in columns)
        {
            foreach (var line in column.Report.Lines)
            {
                var row = ReportLineRow(line, XeroJson.ParseDecimal(line.Value(column.Column)), accounts);
                row["period_start"] = ToDateTime(column.Key);
                row["period_end"] = ToDateTime(MonthEnd(column.Key));
                row["period_label"] = column.Label;
                row["report_generated_at"] = column.Report.UpdatedAt;
                rows.Add(Stamp(context, row, column.Report.UpdatedAt, column.FetchedAt));
            }
        }

        return rows;
    }

    // ---- Bank summary ---------------------------------------------------------------------------------------

    private async Task<List<Row>> LoadBankSummaryAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var (from, to) = MonthlyWindow(context, "period_start", "period_end", wholeYearByDefault: false);
        var months = Months(from, to);
        var calls = new CallBudget(Options.MaxReportCalls);
        calls.Require(months.Count);

        var rows = new List<Row>();
        foreach (var month in months)
        {
            calls.Spend();
            var response = await client.GetAsync(
                context.Tenant,
                "Reports/BankSummary",
                new Dictionary<string, string?> { ["fromDate"] = IsoDate(month), ["toDate"] = IsoDate(MonthEnd(month)) },
                reference: false,
                cancellationToken).ConfigureAwait(false);

            var report = XeroReport.Parse(response.Root);
            var opening = report.Column(1, "Opening Balance");
            var received = report.Column(2, "Cash Received");
            var spent = report.Column(3, "Cash Spent");
            var closing = report.Column(4, "Closing Balance");

            foreach (var line in report.Lines)
            {
                rows.Add(Stamp(context, new Row
                {
                    ["period_start"] = ToDateTime(month),
                    ["period_end"] = ToDateTime(MonthEnd(month)),
                    ["row_type"] = line.Kind,
                    ["account_id"] = line.AccountId,
                    ["bank_account_name"] = line.Label,
                    ["opening_balance"] = XeroJson.ParseDecimal(line.Value(opening)),
                    ["cash_received"] = XeroJson.ParseDecimal(line.Value(received)),
                    ["cash_spent"] = XeroJson.ParseDecimal(line.Value(spent)),
                    ["closing_balance"] = XeroJson.ParseDecimal(line.Value(closing)),
                    ["report_generated_at"] = report.UpdatedAt
                }, report.UpdatedAt, response.FetchedAt));
            }
        }

        return rows;
    }

    // ---- Trial balance --------------------------------------------------------------------------------------

    private async Task<List<Row>> LoadTrialBalanceAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var (from, to) = Window(context.Query, context.Source.DateField);
        var date = to ?? from ?? context.Today;

        var response = await client.GetAsync(
            context.Tenant,
            "Reports/TrialBalance",
            new Dictionary<string, string?> { ["date"] = IsoDate(date) },
            reference: false,
            cancellationToken).ConfigureAwait(false);

        var report = XeroReport.Parse(response.Root);
        var accounts = await ReadAccountIndexAsync(context, cancellationToken).ConfigureAwait(false);
        var debit = report.Column(1, "Debit");
        var credit = report.Column(2, "Credit");
        var ytdDebit = report.Column(3, "YTD Debit");
        var ytdCredit = report.Column(4, "YTD Credit");

        return
        [
            .. report.Lines.Select(line =>
            {
                var row = ReportLineRow(line, value: null, accounts);
                row.Remove("value");
                row["report_date"] = ToDateTime(date);
                row["debit"] = XeroJson.ParseDecimal(line.Value(debit));
                row["credit"] = XeroJson.ParseDecimal(line.Value(credit));
                row["ytd_debit"] = XeroJson.ParseDecimal(line.Value(ytdDebit));
                row["ytd_credit"] = XeroJson.ParseDecimal(line.Value(ytdCredit));
                row["report_generated_at"] = report.UpdatedAt;
                return Stamp(context, row, report.UpdatedAt, response.FetchedAt);
            })
        ];
    }

    // ---- Aged receivables / payables ------------------------------------------------------------------------

    /// <summary>
    /// Xero's aged reports are per contact, and the owner decided (2026-10-01) that the filter must name the
    /// contacts rather than this service walking every contact with a balance — the source is meant to be bound to
    /// an asset's contact.
    /// </summary>
    private async Task<List<Row>> LoadAgedAsync(ReadContext context, string report, CancellationToken cancellationToken)
    {
        var contactIds = GuidKeys(context.Query, "contact_id")
            ?? throw new XeroRequestException(
                "Xero's aged reports are per contact: name the contacts in the filter (contact_id = <ContactID>, or IN (...)).");

        if (contactIds.Count > Options.MaxAgedContacts)
        {
            throw new XeroRequestException(
                $"The filter names {contactIds.Count} contacts and each is one report call (Xero:MaxAgedContacts is {Options.MaxAgedContacts}).");
        }

        var (dateFrom, dateTo) = Window(context.Query, context.Source.DateField);
        var reportDate = dateTo ?? dateFrom ?? context.Today;
        var (invoiceFrom, invoiceTo) = Window(context.Query, "invoice_date");

        var contacts = await client.GetCollectionAsync(
            context.Tenant,
            "Contacts",
            "Contacts",
            new Dictionary<string, string?> { ["IDs"] = string.Join(',', contactIds), ["includeArchived"] = "true" },
            paged: true,
            reference: true,
            cancellationToken).ConfigureAwait(false);

        var names = contacts.Items
            .Select(static contact => (Id: XeroJson.String(contact, "ContactID"), Name: XeroJson.String(contact, "Name")))
            .Where(static pair => pair.Id is not null)
            .GroupBy(static pair => pair.Id!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().Name, StringComparer.OrdinalIgnoreCase);

        var rows = new List<Row>();

        foreach (var contactId in contactIds)
        {
            var response = await client.GetAsync(
                context.Tenant,
                $"Reports/{report}",
                new Dictionary<string, string?>
                {
                    ["contactId"] = contactId,
                    ["date"] = IsoDate(reportDate),
                    ["fromDate"] = invoiceFrom is { } f ? IsoDate(f) : null,
                    ["toDate"] = invoiceTo is { } t ? IsoDate(t) : null
                },
                reference: false,
                cancellationToken).ConfigureAwait(false);

            var parsed = XeroReport.Parse(response.Root);
            var dateColumn = parsed.Column(0, "Date");
            var numberColumn = parsed.Column(1, "Number", "Reference", "Invoice Number");
            var dueDateColumn = parsed.Column(2, "Due Date");
            var totalColumn = parsed.Column(4, "Total");
            var paidColumn = parsed.Column(5, "Paid", "Received");
            var creditedColumn = parsed.Column(6, "Credited");
            var dueColumn = parsed.Column(7, "Due", "Amount Due", "Balance");
            var contactName = names.GetValueOrDefault(contactId) ?? (parsed.Titles.Count > 1 ? parsed.Titles[1] : null);

            foreach (var line in parsed.Lines)
            {
                var invoiceId = line.Cells.Select(static cell => cell.Attribute("invoiceID")).FirstOrDefault(static id => id is not null);
                var kind = AgedRowKind(line, invoiceId);
                var dueDate = kind == "invoice" ? XeroJson.ParseDate(line.Value(dueDateColumn)) : null;
                var amountDue = XeroJson.ParseDecimal(line.Value(dueColumn));

                int? daysOverdue = null;
                string? bucket = null;
                if (kind == "invoice")
                {
                    var overdue = dueDate is { } due && amountDue > 0
                        ? Math.Max(0, reportDate.DayNumber - DateOnly.FromDateTime(due).DayNumber)
                        : 0;
                    daysOverdue = overdue;
                    bucket = amountDue > 0 ? AgeingBucket(overdue) : null;
                }

                rows.Add(Stamp(context, new Row
                {
                    ["report_date"] = ToDateTime(reportDate),
                    ["contact_id"] = contactId,
                    ["contact_name"] = contactName,
                    ["row_type"] = kind,
                    ["invoice_id"] = invoiceId,
                    ["invoice_number"] = kind == "invoice" ? line.Value(numberColumn) : null,
                    ["invoice_date"] = kind is "invoice" or "opening_balance" ? XeroJson.ParseDate(line.Value(dateColumn)) : null,
                    ["due_date"] = dueDate,
                    ["days_overdue"] = daysOverdue,
                    ["ageing_bucket"] = bucket,
                    ["total"] = XeroJson.ParseDecimal(line.Value(totalColumn)),
                    ["paid"] = XeroJson.ParseDecimal(line.Value(paidColumn)),
                    ["credited"] = XeroJson.ParseDecimal(line.Value(creditedColumn)),
                    ["amount_due"] = amountDue,
                    ["report_generated_at"] = parsed.UpdatedAt
                }, parsed.UpdatedAt, response.FetchedAt));
            }
        }

        return rows;
    }

    private static string AgedRowKind(XeroReportLine line, string? invoiceId)
    {
        if (line.IsSummary)
        {
            return line.Cells.Any(static cell => string.Equals(cell.Value, "Closing Balance", StringComparison.OrdinalIgnoreCase))
                ? "closing_balance"
                : "total";
        }

        if (invoiceId is not null)
        {
            return "invoice";
        }

        return line.Cells.Any(static cell => string.Equals(cell.Value, "Opening Balance", StringComparison.OrdinalIgnoreCase))
            ? "opening_balance"
            : "other";
    }

    private static string AgeingBucket(int daysOverdue) => daysOverdue switch
    {
        <= 0 => "current",
        <= 30 => "1-30",
        <= 60 => "31-60",
        <= 90 => "61-90",
        _ => "91+"
    };

    // ---- Series reading -------------------------------------------------------------------------------------

    /// <param name="Key">The month (its first day) or the as-of date this column is for.</param>
    private sealed record ReportColumn(DateOnly Key, string? Label, XeroReport Report, int Column, DateTime FetchedAt);

    /// <summary>
    /// One column per key, read twelve at a time: <paramref name="buildQuery"/> turns a chunk of keys (ascending)
    /// into one call's parameters, each answered column is matched to a key by its label
    /// (<paramref name="keyFromLabel"/>) or else its position (<paramref name="keyFromIndex"/>), and a key no column
    /// answered is read again on its own.
    /// </summary>
    private async Task<List<ReportColumn>> ReadSeriesAsync(
        ReadContext context,
        string path,
        IReadOnlyList<DateOnly> keys,
        Func<IReadOnlyList<DateOnly>, Dictionary<string, string?>> buildQuery,
        Func<(DateOnly Date, bool HasDay), DateOnly> keyFromLabel,
        Func<IReadOnlyList<DateOnly>, int, DateOnly> keyFromIndex,
        CallBudget calls,
        CancellationToken cancellationToken,
        Func<IReadOnlyList<DateOnly>, IEnumerable<IReadOnlyList<DateOnly>>>? chunker = null)
    {
        var found = new Dictionary<DateOnly, ReportColumn>();
        var chunks = chunker?.Invoke(keys) ?? keys.Chunk(MaxPeriodsPerCall);

        foreach (var chunk in chunks)
        {
            calls.Spend();
            var response = await client.GetAsync(context.Tenant, path, buildQuery(chunk), reference: false, cancellationToken).ConfigureAwait(false);
            var report = XeroReport.Parse(response.Root);
            Match(report, response.FetchedAt, chunk, found, keyFromLabel, keyFromIndex);

            foreach (var missing in chunk.Where(key => !found.ContainsKey(key)).ToList())
            {
                logger.LogDebug("{Path}: no column answered {Key}; reading it on its own.", path, missing);
                calls.Spend();
                var single = await client.GetAsync(context.Tenant, path, buildQuery([missing]), reference: false, cancellationToken).ConfigureAwait(false);
                Match(XeroReport.Parse(single.Root), single.FetchedAt, [missing], found, keyFromLabel, keyFromIndex);
            }
        }

        return [.. found.Values.OrderBy(static column => column.Key)];
    }

    private static void Match(
        XeroReport report,
        DateTime fetchedAt,
        IReadOnlyList<DateOnly> chunk,
        Dictionary<DateOnly, ReportColumn> found,
        Func<(DateOnly Date, bool HasDay), DateOnly> keyFromLabel,
        Func<IReadOnlyList<DateOnly>, int, DateOnly> keyFromIndex)
    {
        var columnCount = report.Header.Count > 0
            ? report.Header.Count
            : report.Lines.Select(static line => line.Cells.Count).DefaultIfEmpty(0).Max();

        // A call for one key answers that key whatever its column is labelled.
        if (chunk.Count == 1)
        {
            if (columnCount > 1 && !found.ContainsKey(chunk[0]))
            {
                found[chunk[0]] = new ReportColumn(chunk[0], Label(report, 1), report, 1, fetchedAt);
            }

            return;
        }

        for (var column = 1; column < columnCount; column++)
        {
            var label = Label(report, column);
            var key = XeroReport.ParseLabel(label) is { } parsed ? keyFromLabel(parsed) : keyFromIndex(chunk, column - 1);

            if (chunk.Contains(key) && !found.ContainsKey(key))
            {
                found[key] = new ReportColumn(key, label, report, column, fetchedAt);
            }
        }
    }

    private static string? Label(XeroReport report, int column) =>
        column < report.Header.Count && !string.IsNullOrWhiteSpace(report.Header[column]) ? report.Header[column] : null;

    /// <summary>
    /// The month a P&amp;L column is for — only when it ends on that month's last day. Xero's comparison columns
    /// repeat the base period's day numbers rather than taking calendar months: measured on the Demo Company
    /// (2026-10-01), a September base gives a column "30 Aug 26" that is 1–30 August (Total Income 11 258.69 against
    /// 13 478.64 for the whole month). Such a column answers nothing here, and its month is read again on its own.
    /// </summary>
    private static DateOnly ProfitAndLossMonth((DateOnly Date, bool HasDay) label) =>
        !label.HasDay || label.Date == MonthEnd(label.Date) ? FirstOfMonth(label.Date) : label.Date;

    /// <summary>
    /// Chunks whose newest month has 31 days, so every comparison column is a whole month: from a 31-day base Xero
    /// clamps a shorter month to its own end (base August: "31 Jul 26", "30 Jun 26"). A short month that would be a
    /// base is read alone; no two short months are adjacent, so that costs at most one call per chunk.
    /// </summary>
    private static IEnumerable<IReadOnlyList<DateOnly>> ProfitAndLossChunks(IReadOnlyList<DateOnly> months)
    {
        var index = months.Count - 1;
        while (index >= 0)
        {
            if (DateTime.DaysInMonth(months[index].Year, months[index].Month) < 31)
            {
                yield return [months[index]];
                index--;
                continue;
            }

            var start = Math.Max(0, index - MaxPeriodsPerCall + 1);
            yield return [.. months.Skip(start).Take(index - start + 1)];
            index = start - 1;
        }
    }

    // ---- Tracking -------------------------------------------------------------------------------------------

    /// <summary>One P&amp;L series: the report narrowed to up to one option per tracking slot, or not narrowed at all.</summary>
    private sealed record TrackingSelection(Dictionary<string, string?> Parameters, Row Fields)
    {
        public static readonly TrackingSelection None = new(new Dictionary<string, string?>(), new Row());

        public void Apply(Row row)
        {
            foreach (var (field, value) in Fields)
            {
                row[field] = value;
            }
        }
    }

    /// <summary>
    /// The options the filter names for <c>tracking_option_1_id</c> / <c>tracking_option_2_id</c>, one series per
    /// combination. An option the organisation does not have answers nothing rather than an unnarrowed report.
    /// </summary>
    private async Task<List<TrackingSelection>> TrackingSelectionsAsync(ReadContext context, CancellationToken cancellationToken)
    {
        var first = GuidKeys(context.Query, "tracking_option_1_id");
        var second = GuidKeys(context.Query, "tracking_option_2_id");
        if (first is null && second is null)
        {
            return [TrackingSelection.None];
        }

        var categories = await ReadTrackingCategoriesAsync(context, cancellationToken).ConfigureAwait(false);
        var options = new Dictionary<string, (string? CategoryId, string? Category, string? Option)>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in categories.Items)
        {
            foreach (var option in XeroJson.Array(category, "Options"))
            {
                if (XeroJson.String(option, "TrackingOptionID") is { } id)
                {
                    options[id] = (XeroJson.String(category, "TrackingCategoryID"), XeroJson.String(category, "Name"), XeroJson.String(option, "Name"));
                }
            }
        }

        List<string?> Known(IReadOnlyList<string>? ids) =>
            ids is null ? [null] : [.. ids.Where(options.ContainsKey)];

        var selections = new List<TrackingSelection>();
        foreach (var one in Known(first))
        {
            foreach (var two in Known(second))
            {
                var parameters = new Dictionary<string, string?>(StringComparer.Ordinal);
                var fields = new Row(StringComparer.OrdinalIgnoreCase);

                if (one is not null)
                {
                    var option = options[one];
                    parameters["trackingCategoryID"] = option.CategoryId;
                    parameters["trackingOptionID"] = one;
                    fields["tracking_category_1_id"] = option.CategoryId;
                    fields["tracking_category_1"] = option.Category;
                    fields["tracking_option_1_id"] = one;
                    fields["tracking_option_1"] = option.Option;
                }

                if (two is not null)
                {
                    var option = options[two];
                    parameters["trackingCategoryID2"] = option.CategoryId;
                    parameters["trackingOptionID2"] = two;
                    fields["tracking_category_2_id"] = option.CategoryId;
                    fields["tracking_category_2"] = option.Category;
                    fields["tracking_option_2_id"] = two;
                    fields["tracking_option_2"] = option.Option;
                }

                selections.Add(new TrackingSelection(parameters, fields));
            }
        }

        return selections;
    }

    // ---- Helpers --------------------------------------------------------------------------------------------

    /// <summary>A report line laid out by account: section, kind, label, the account and its value.</summary>
    private static Row ReportLineRow(XeroReportLine line, decimal? value, Dictionary<string, (string? Code, string? Name)> accounts)
    {
        var accountId = line.AccountId;
        var known = accountId is not null && accounts.TryGetValue(accountId, out var entry) ? entry : default;

        return new Row(StringComparer.OrdinalIgnoreCase)
        {
            ["section"] = line.Section,
            ["row_type"] = line.Kind,
            ["label"] = line.Label,
            ["account_id"] = accountId,
            ["account_code"] = known.Code,
            ["account_name"] = accountId is null ? null : known.Name ?? line.Label,
            ["value"] = value
        };
    }

    /// <summary>
    /// The months a monthly report covers: the filter's bounds on the start field (the end field's upper bound when
    /// the start has none), defaulting to the current financial year — to date, or whole for a budget.
    /// </summary>
    private (DateOnly From, DateOnly To) MonthlyWindow(ReadContext context, string startField, string endField, bool wholeYearByDefault)
    {
        var (from, to) = Window(context.Query, startField);
        to ??= Window(context.Query, endField).To;

        var anchor = to ?? from ?? context.Today;
        var lower = from ?? FinancialYearStart(context, anchor);
        var upper = to ?? (wholeYearByDefault ? FinancialYearStart(context, lower).AddYears(1).AddDays(-1) : context.Today);

        if (upper < lower)
        {
            upper = lower;
        }

        var count = (upper.Year - lower.Year) * 12 + upper.Month - lower.Month + 1;
        if (count > Options.MaxReportMonths)
        {
            throw TooManyMonths(count);
        }

        return (lower, upper);
    }

    private static List<DateOnly> Months(DateOnly from, DateOnly to)
    {
        var months = new List<DateOnly>();
        for (var month = FirstOfMonth(from); month <= to; month = month.AddMonths(1))
        {
            months.Add(month);
        }

        return months;
    }

    private static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    private static DateOnly MonthEnd(DateOnly date) => new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));

    private XeroRequestException TooManyMonths(int count) =>
        new($"The window covers {count} months; a monthly report reads at most {Options.MaxReportMonths} (Xero:MaxReportMonths). Narrow the date bounds in the filter.");

    /// <summary>Report calls one query may still make (<c>Xero:MaxReportCalls</c>).</summary>
    private sealed class CallBudget(int limit)
    {
        private int spent;

        public void Require(int calls)
        {
            if (spent + calls > limit)
            {
                throw Exceeded(spent + calls);
            }
        }

        public void Spend()
        {
            if (++spent > limit)
            {
                throw Exceeded(spent);
            }
        }

        private XeroRequestException Exceeded(int calls) =>
            new($"This query needs more than {limit} report calls to Xero (Xero:MaxReportCalls; at least {calls}). Narrow the date window or the tracking options in the filter.");
    }
}
