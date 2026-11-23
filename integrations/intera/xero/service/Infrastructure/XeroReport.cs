using System.Globalization;
using System.Text.Json;

namespace Intera.Xero.Api.Infrastructure;

public sealed record XeroReportCell(string? Value, IReadOnlyDictionary<string, string> Attributes)
{
    /// <summary>The cell's account id, under whichever name the report uses (<c>account</c>, <c>accountID</c>).</summary>
    public string? AccountId => Attributes.TryGetValue("account", out var id) || Attributes.TryGetValue("accountID", out id) ? id : null;

    public string? Attribute(string id) => Attributes.TryGetValue(id, out var value) ? value : null;
}

/// <param name="Section">The enclosing section's title; null outside a section or for an untitled one.</param>
/// <param name="RowType">Xero's own: <c>Row</c> or <c>SummaryRow</c>.</param>
public sealed record XeroReportLine(string? Section, string RowType, IReadOnlyList<XeroReportCell> Cells)
{
    public string? Label => Cells.Count > 0 ? Cells[0].Value : null;

    /// <summary>The first account id any cell carries.</summary>
    public string? AccountId => Cells.Select(static cell => cell.AccountId).FirstOrDefault(static id => id is not null);

    public bool IsSummary => string.Equals(RowType, "SummaryRow", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <c>account</c> for a line of one account, <c>total</c> for a section's total, <c>calculated</c> for a line
    /// computed from others (Gross Profit, Net Profit) — the three a dashboard has to tell apart, since only the
    /// first may be summed.
    /// </summary>
    public string Kind => IsSummary ? "total" : AccountId is not null ? "account" : "calculated";

    public string? Value(int column) => column >= 0 && column < Cells.Count ? Cells[column].Value : null;
}

/// <summary>
/// One report from <c>/Reports/*</c>, flattened: the header labels, and every row with the section it sits in.
/// </summary>
/// <remarks>
/// Xero's report JSON is a layout, not a table — a <c>Header</c> row, then <c>Section</c>s holding <c>Row</c>s and
/// <c>SummaryRow</c>s, every value a string. What a column means is only in the header label, and the column order is
/// not the same in every report (the balance sheet lists the newest date first, the budget summary the oldest), so
/// the readers here map columns by their labels and use positions only as a fallback.
/// </remarks>
public sealed record XeroReport(
    string? Name,
    IReadOnlyList<string> Titles,
    IReadOnlyList<string> Header,
    IReadOnlyList<XeroReportLine> Lines,
    DateTime? UpdatedAt)
{
    private static readonly string[] DayFormats = ["d MMM yyyy", "dd MMM yyyy", "d MMM yy", "dd MMM yy", "d MMMM yyyy", "yyyy-MM-dd"];

    private static readonly string[] MonthFormats = ["MMM yyyy", "MMM-yy", "MMM yy", "MMMM yyyy", "MMM-yyyy", "yyyy-MM"];

    public static XeroReport Parse(JsonElement root)
    {
        if (XeroJson.First(root, "Reports") is not { } report)
        {
            throw new XeroUpstreamException("Xero answered a report request without a report.");
        }

        var header = new List<string>();
        var lines = new List<XeroReportLine>();
        Walk(XeroJson.Array(report, "Rows"), section: null, header, lines);

        return new XeroReport(
            XeroJson.String(report, "ReportName"),
            [.. XeroJson.Array(report, "ReportTitles").Select(static title => title.ValueKind == JsonValueKind.String ? title.GetString() ?? string.Empty : string.Empty)],
            header,
            lines,
            XeroJson.Timestamp(report, "UpdatedDateUTC"));
    }

    /// <summary>The index of the first column whose header label is one of <paramref name="labels"/>, else <paramref name="fallback"/>.</summary>
    public int Column(int fallback, params string[] labels)
    {
        for (var index = 0; index < Header.Count; index++)
        {
            if (labels.Any(label => string.Equals(Header[index].Trim(), label, StringComparison.OrdinalIgnoreCase)))
            {
                return index;
            }
        }

        return fallback;
    }

    /// <summary>A header label read as a date: a full date (<c>30 Apr 2019</c>), or a month (<c>Jan-20</c>) as its first day.</summary>
    public static (DateOnly Date, bool HasDay)? ParseLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        var text = label.Trim();
        if (DateTime.TryParseExact(text, DayFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return (DateOnly.FromDateTime(day), true);
        }

        if (DateTime.TryParseExact(text, MonthFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
        {
            return (new DateOnly(month.Year, month.Month, 1), false);
        }

        return null;
    }

    private static void Walk(IEnumerable<JsonElement> rows, string? section, List<string> header, List<XeroReportLine> lines)
    {
        foreach (var row in rows)
        {
            var rowType = XeroJson.String(row, "RowType") ?? string.Empty;

            switch (rowType)
            {
                case "Header":
                    if (header.Count == 0)
                    {
                        header.AddRange(Cells(row).Select(static cell => cell.Value ?? string.Empty));
                    }

                    break;

                case "Section":
                    Walk(XeroJson.Array(row, "Rows"), XeroJson.String(row, "Title") ?? section, header, lines);
                    break;

                default:
                    lines.Add(new XeroReportLine(section, rowType, Cells(row)));
                    break;
            }
        }
    }

    private static List<XeroReportCell> Cells(JsonElement row) =>
    [
        .. XeroJson.Array(row, "Cells").Select(static cell => new XeroReportCell(
            XeroJson.String(cell, "Value"),
            XeroJson.Array(cell, "Attributes")
                .Select(static attribute => (Id: XeroJson.String(attribute, "Id"), Value: XeroJson.String(attribute, "Value")))
                .Where(static pair => pair.Id is not null && pair.Value is not null)
                .GroupBy(static pair => pair.Id!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.First().Value!, StringComparer.OrdinalIgnoreCase)))
    ];
}
