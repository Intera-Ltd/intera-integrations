using Intera.Soft1.Api.Clients;
using Intera.Soft1.Api.Infrastructure;
using Intera.Soft1.Api.Options;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text;

namespace Intera.Soft1.Api.Services;

using Row = Dictionary<string, object?>;

/// <summary>What one read is narrowed to.</summary>
/// <param name="From">First day of the reporting window, sent to a browser's date filter.</param>
/// <param name="To">Last day of the reporting window (inclusive).</param>
/// <param name="FiscalYears">The years a per-year table is read for, one call each.</param>
/// <param name="Series">
/// Document series to read, one browser call per series so that every row knows its own — a default document
/// browser does not show the series. Null reads without a series filter.
/// </param>
public sealed record Soft1ReadScope(
    DateOnly? From = null,
    DateOnly? To = null,
    IReadOnlyList<int>? FiscalYears = null,
    IReadOnlyList<string>? Series = null)
{
    public static readonly Soft1ReadScope Everything = new();
}

/// <summary>What a probe of one read found: which columns matched, which the browser has that nothing reads.</summary>
public sealed record Soft1Probe(
    string Read,
    string Mode,
    string Object,
    string List,
    string Filters,
    int? TotalCount,
    IReadOnlyList<string> SourceColumns,
    IReadOnlyDictionary<string, string?> MatchedColumns,
    IReadOnlyList<string> UnreadColumns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> SampleRows,
    string? Error);

public interface ISoft1Reader
{
    Task<IReadOnlyList<Row>> ReadAsync(string readId, Soft1ReadScope scope, CancellationToken cancellationToken);

    Task<Soft1Probe> ProbeAsync(string readId, CancellationToken cancellationToken);
}

/// <summary>
/// Runs the reads of <see cref="Soft1ReadCatalog"/>: a browser page by page, a table once per key value, and
/// turns what Soft1 sends into typed row values.
/// </summary>
public sealed class Soft1Reader(
    ISoft1Client client,
    Soft1ReadCatalog catalog,
    IOptions<Soft1Options> options) : ISoft1Reader
{
    private const string ZoomInfo = "ZOOMINFO";

    private Soft1Options Options => options.Value;

    public async Task<IReadOnlyList<Row>> ReadAsync(string readId, Soft1ReadScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var read = catalog.Get(readId);

        return read.Mode == Soft1ReadMode.Browser
            ? await ReadBrowserAsync(read, scope, cancellationToken).ConfigureAwait(false)
            : await ReadTableAsync(read, scope, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Soft1Probe> ProbeAsync(string readId, CancellationToken cancellationToken)
    {
        var read = catalog.Get(readId);

        try
        {
            if (read.Mode == Soft1ReadMode.Browser)
            {
                var filters = BuildFilters(read, DefaultScope(), series: null);
                var browser = await client.PeekBrowserAsync(read.Object, read.List, filters, 5, cancellationToken).ConfigureAwait(false);
                var index = IndexColumns(browser.Fields);
                var matched = read.Columns.ToDictionary(
                    static column => column.Field,
                    column => column.Kind == Soft1ValueKind.RecordKey ? ZoomInfo : column.Candidates.FirstOrDefault(c => index.ContainsKey(c.TrimStart('='))),
                    StringComparer.OrdinalIgnoreCase);
                var used = new HashSet<string>(matched.Values.Where(static name => name is not null).Select(static name => name!.TrimStart('=')), StringComparer.OrdinalIgnoreCase);

                return new Soft1Probe(
                    read.Id, "Browser", read.Object, read.List, filters, browser.TotalCount, browser.Fields, matched,
                    [.. browser.Fields.Where(name => !used.Contains(name))],
                    [.. browser.Rows.Select(values => (IReadOnlyDictionary<string, object?>)MapBrowserRow(read, index, values))],
                    null);
            }

            var keyValue = ResolveKeyValue(read, await CompanyAsync(cancellationToken).ConfigureAwait(false), DateTime.Today.Year);
            var fields = TableFields(read);
            var rows = await client.ReadTableAsync(read.Object, read.KeyName, keyValue, fields, cancellationToken).ConfigureAwait(false);

            return new Soft1Probe(
                read.Id, "Table", read.Object, string.Empty, $"{read.KeyName}={keyValue}", rows.Count, fields,
                read.Columns.ToDictionary(static column => column.Field, static column => (string?)column.Candidates.FirstOrDefault(), StringComparer.OrdinalIgnoreCase),
                [],
                [.. rows.Take(5).Select(row => (IReadOnlyDictionary<string, object?>)MapTableRow(read, row))],
                null);
        }
        catch (Exception exception) when (exception is Soft1RequestException or Soft1QueryTooBroadException)
        {
            return new Soft1Probe(read.Id, read.Mode.ToString(), read.Object, read.List, read.Filters, null, [],
                new Dictionary<string, string?>(), [], [], exception.Message);
        }
    }

    private async Task<IReadOnlyList<Row>> ReadBrowserAsync(Soft1Read read, Soft1ReadScope scope, CancellationToken cancellationToken)
    {
        var maxRows = Math.Max(1, Options.MaxRowsPerRead);

        // One call per series: the series is what tells an invoice from an order and names the document type,
        // and a default document browser does not show it. With no series filter configured the whole browser is
        // read once and the rows carry whatever series column it has.
        if (scope.Series is { Count: > 0 } series && read.SeriesFilter is not null)
        {
            var tasks = series
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(async value =>
                {
                    var browser = await client.ReadBrowserAsync(
                        read.Object, read.List, BuildFilters(read, scope, value), maxRows, cancellationToken).ConfigureAwait(false);
                    var rows = MapBrowser(read, browser);
                    foreach (var row in rows)
                    {
                        row["series_id"] = value;
                    }

                    return rows;
                })
                .ToList();

            var parts = await Task.WhenAll(tasks).ConfigureAwait(false);
            var total = parts.Sum(static part => part.Count);

            if (total > maxRows)
            {
                throw new Soft1QueryTooBroadException(
                    $"The Soft1 browser {read.Object} holds {total} rows for this query, more than Soft1:MaxRowsPerRead ({maxRows}).");
            }

            return [.. parts.SelectMany(static part => part)];
        }

        var whole = await client.ReadBrowserAsync(
            read.Object, read.List, BuildFilters(read, scope, series: null), maxRows, cancellationToken).ConfigureAwait(false);

        return MapBrowser(read, whole);
    }

    private async Task<IReadOnlyList<Row>> ReadTableAsync(Soft1Read read, Soft1ReadScope scope, CancellationToken cancellationToken)
    {
        var company = await CompanyAsync(cancellationToken).ConfigureAwait(false);
        var fields = TableFields(read);
        var years = read.PerFiscalYear ? scope.FiscalYears ?? [DateTime.Today.Year] : [0];

        var parts = await Task.WhenAll(years.Select(year => client.ReadTableAsync(
            read.Object, read.KeyName, ResolveKeyValue(read, company, year), fields, cancellationToken))).ConfigureAwait(false);

        var rows = new List<Row>();
        foreach (var part in parts)
        {
            foreach (var values in part)
            {
                var row = MapTableRow(read, values);

                // The key narrows by one column only; the company is checked here for tables keyed otherwise.
                if (read.CompanyColumn is not null && company is not null
                    && row.TryGetValue(read.CompanyColumn, out var owner) && owner is string text
                    && !string.Equals(text, company, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                rows.Add(row);
            }
        }

        if (rows.Count > Options.MaxRowsPerRead)
        {
            throw new Soft1QueryTooBroadException(
                $"The Soft1 table {read.Object} returned {rows.Count} rows for this query, more than Soft1:MaxRowsPerRead ({Options.MaxRowsPerRead}).");
        }

        return rows;
    }

    private async Task<string?> CompanyAsync(CancellationToken cancellationToken) =>
        (await client.GetSessionAsync(cancellationToken).ConfigureAwait(false)).Company;

    private static string ResolveKeyValue(Soft1Read read, string? company, int fiscalYear)
    {
        if (read.KeyValue.Contains("{company}", StringComparison.OrdinalIgnoreCase) && company is null)
        {
            throw new Soft1RequestException(
                $"The Soft1 read '{read.Id}' is keyed by company, and the session does not know its company. Set Soft1:Company.");
        }

        return read.KeyValue
            .Replace("{company}", company ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{fiscal_year}", fiscalYear.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> TableFields(Soft1Read read) =>
    [
        .. read.Columns
            .Where(static column => column.Kind != Soft1ValueKind.RecordKey && column.Candidates.Count > 0)
            .Select(static column => column.Candidates[0].TrimStart('='))
            .Distinct(StringComparer.OrdinalIgnoreCase)
    ];

    private Soft1ReadScope DefaultScope()
    {
        var years = Math.Max(1, Options.DefaultFiscalYears);
        return new Soft1ReadScope(new DateOnly(DateTime.Today.Year - years + 1, 1, 1), DateOnly.FromDateTime(DateTime.Today));
    }

    /// <summary>
    /// The read's fixed filters, the date window and the series, <c>&amp;</c>-joined.
    /// </summary>
    /// <remarks>
    /// The window is always sent whole — both bounds — and the upper bound one day late: BlackBook describes a
    /// FROM/TO pair as "&gt;= DATEFROM and &lt; (DATETO-1)", which reads as exclusive at best and a day short at
    /// worst. The in-memory pass drops the extra day, so a superset costs nothing but a few rows.
    /// </remarks>
    private string BuildFilters(Soft1Read read, Soft1ReadScope scope, string? series)
    {
        var builder = new StringBuilder(read.Filters.Trim().Trim('&'));

        if (read.DateFilter is not null)
        {
            var fallback = DefaultScope();
            var from = scope.From ?? fallback.From!.Value;
            var to = (scope.To ?? DateOnly.FromDateTime(DateTime.Today).AddYears(1)).AddDays(1);

            Append(builder, read.DateFilter, Format(from));
            Append(builder, read.DateFilter + "_TO", Format(to));
        }

        if (series is not null && read.SeriesFilter is not null)
        {
            Append(builder, read.SeriesFilter, series);
        }

        return builder.ToString();

        static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static void Append(StringBuilder builder, string name, string value)
    {
        if (builder.Length > 0)
        {
            builder.Append('&');
        }

        builder.Append(name).Append('=').Append(value);
    }

    private static Dictionary<string, int> IndexColumns(IReadOnlyList<string> fields)
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < fields.Count; i++)
        {
            index.TryAdd(fields[i], i);
        }

        return index;
    }

    private static List<Row> MapBrowser(Soft1Read read, Soft1Browser browser)
    {
        var index = IndexColumns(browser.Fields);
        return [.. browser.Rows.Select(values => MapBrowserRow(read, index, values))];
    }

    private static Row MapBrowserRow(Soft1Read read, Dictionary<string, int> index, string?[] values)
    {
        var row = new Row(read.Columns.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var column in read.Columns)
        {
            if (column.Kind == Soft1ValueKind.RecordKey)
            {
                // BlackBook: "ZOOMINFO indicates the id of the main table of the object" — "CUSTOMER;47",
                // "01351;594". Every published example has it first.
                var position = index.TryGetValue(ZoomInfo, out var zoom) ? zoom : 0;
                var raw = position < values.Length ? values[position] : null;
                row[column.Field] = raw is null ? null : Soft1Values.Text(raw[(raw.LastIndexOf(';') + 1)..]);
                continue;
            }

            object? value = null;
            foreach (var candidate in column.Candidates)
            {
                var whole = candidate.StartsWith('=');
                var name = whole ? candidate[1..] : candidate;

                if (!index.TryGetValue(name, out var position) || position >= values.Length)
                {
                    continue;
                }

                value = Convert(values[position], whole && column.Kind is Soft1ValueKind.Id or Soft1ValueKind.Label ? Soft1ValueKind.Text : column.Kind);
                if (value is not null)
                {
                    break;
                }
            }

            row[column.Field] = value;
        }

        return row;
    }

    private static Row MapTableRow(Soft1Read read, IReadOnlyDictionary<string, string?> values)
    {
        var row = new Row(read.Columns.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var column in read.Columns)
        {
            var name = column.Candidates.Count > 0 ? column.Candidates[0].TrimStart('=') : null;
            row[column.Field] = name is not null && values.TryGetValue(name, out var raw) ? Convert(raw, column.Kind) : null;
        }

        return row;
    }

    /// <summary>Text as Soft1 sent it, into the kind's type. Selector values arrive as <c>"id|caption"</c>.</summary>
    public static object? Convert(string? raw, Soft1ValueKind kind)
    {
        var text = Soft1Values.Text(raw);
        if (text is null)
        {
            return null;
        }

        var bar = text.IndexOf('|', StringComparison.Ordinal);

        return kind switch
        {
            Soft1ValueKind.Number => Soft1Values.Decimal(bar >= 0 ? text[..bar] : text),
            Soft1ValueKind.Date => Soft1Values.Date(text),
            Soft1ValueKind.Flag => Soft1Values.Flag(bar >= 0 ? text[..bar] : text),
            Soft1ValueKind.Id => Soft1Values.Text(bar >= 0 ? text[..bar] : text),
            Soft1ValueKind.Label => bar >= 0 ? Soft1Values.Text(text[(bar + 1)..]) : null,
            _ => text
        };
    }
}
