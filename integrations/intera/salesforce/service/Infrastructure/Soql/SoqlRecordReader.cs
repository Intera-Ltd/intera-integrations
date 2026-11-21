using System.Globalization;
using System.Text.Json;
using Intera.Common.Infrastructure.Query;
using Intera.Common.Models.Query;

namespace Intera.Salesforce.Api.Infrastructure.Soql;

/// <summary>
/// Turns Salesforce's records into the query DSL's row dictionaries, following the
/// <see cref="SoqlProjection"/> the query builder produced alongside the SOQL.
/// </summary>
/// <remarks>
/// Salesforce does not answer with the caller's aliases, and the two record shapes it uses differ:
/// a plain query returns each field under its own path — nested for a parent traversal, so
/// <c>Account.Name</c> arrives as <c>{"Account":{"Name":"…"}}</c> — while an aggregate query returns
/// each expression under the alias the SELECT gave it. Both are read here through the same
/// projection, which is why the SELECT and the mapping back cannot drift apart.
/// </remarks>
public static class SoqlRecordReader
{
    /// <summary>Salesforce's own metadata object, present on every record and never a column.</summary>
    private const string AttributesProperty = "attributes";

    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> Read(
        IReadOnlyList<JsonElement> records,
        SoqlProjection projection)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(projection);

        var rows = new List<IReadOnlyDictionary<string, object?>>(records.Count);

        foreach (var record in records)
        {
            var row = new Dictionary<string, object?>(projection.Items.Count, StringComparer.OrdinalIgnoreCase);

            foreach (var item in projection.Items)
            {
                row[item.Alias] = ReadValue(record, item.Source);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static object? ReadValue(JsonElement record, SoqlValueSource source)
    {
        return source switch
        {
            SoqlPathValue path => ConvertScalar(ResolvePath(record, path.Path), path.Kind),
            SoqlAliasValue alias => ConvertScalar(ResolveProperty(record, alias.SoqlAlias), null),

            // COUNT is what the SELECT actually asked for; "at least one" is the answer the DSL
            // promises, and the same boolean QuerySqlBuilder casts to bit for the same node.
            SoqlExistsValue exists => ToCount(ResolveProperty(record, exists.SoqlAlias)) > 0,
            SoqlLiteralValue literal => literal.Value,
            SoqlMathValue math => EvaluateMath(
                ReadValue(record, math.Left),
                ReadValue(record, math.Right),
                math.Operator),
            SoqlDateDiffValue dateDiff => EvaluateDateDiff(
                dateDiff.Unit,
                ReadValue(record, dateDiff.Start),
                ReadValue(record, dateDiff.End),
                dateDiff.Zone,
                dateDiff.WeekStart),
            _ => throw new ArgumentException($"Unsupported value source '{source.GetType().Name}'.")
        };
    }

    /// <summary>
    /// Walks a dotted SOQL path through the nesting a parent traversal produces. A parent that is not
    /// set reads as <see langword="null"/> rather than as a missing column — the row exists, the
    /// relationship simply is not filled in.
    /// </summary>
    private static JsonElement? ResolvePath(JsonElement record, string path)
    {
        var current = record;

        foreach (var segment in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object
                || !current.TryGetProperty(segment, out var next)
                || next.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    private static JsonElement? ResolveProperty(JsonElement record, string property)
    {
        if (record.ValueKind != JsonValueKind.Object
            || !record.TryGetProperty(property, out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value;
    }

    /// <summary>
    /// Converts one JSON scalar to the CLR value the response envelope should carry.
    /// </summary>
    /// <remarks>
    /// <paramref name="kind"/> is known only for a field read by path; an aggregate's type is not
    /// declared anywhere, so its value passes through as JSON gave it. The date branches exist
    /// because Salesforce writes a DateTime as <c>2026-09-03T10:00:00.000+0000</c> — a legal ISO 8601
    /// basic-offset form that some consumers do not parse — and turning it into a
    /// <see cref="DateTimeOffset"/> makes this API emit the same extended spelling the rest of the
    /// solution does.
    /// </remarks>
    private static object? ConvertScalar(JsonElement? element, SoqlFieldKind? kind)
    {
        if (element is not { } value)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => ReadNumber(value),
            JsonValueKind.String => ReadString(value.GetString(), kind),

            // A nested object or an array here means the record carried a sub-query result or a
            // relationship the projection did not expect. Handing back the raw JSON text is more
            // useful than dropping it, and it is visible enough to be reported.
            _ => value.GetRawText()
        };
    }

    private static object ReadNumber(JsonElement value)
    {
        if (value.TryGetInt64(out var integer))
        {
            return integer;
        }

        return value.TryGetDecimal(out var number) ? number : value.GetDouble();
    }

    private static object? ReadString(string? text, SoqlFieldKind? kind)
    {
        if (text is null)
        {
            return null;
        }

        return kind switch
        {
            SoqlFieldKind.Date when DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) => day,
            SoqlFieldKind.DateTime when DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var instant) => instant,
            _ => text
        };
    }

    private static long ToCount(JsonElement? element)
    {
        return element is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out var count)
            ? count
            : 0L;
    }

    /// <summary>
    /// The arithmetic SOQL cannot do in a SELECT, applied to values Salesforce already returned.
    /// </summary>
    /// <remarks>
    /// Deliberately identical to <c>InMemoryQueryEngine.EvaluateMath</c>, down to the null result for
    /// a non-numeric operand and for a division by zero (canonical contract): the same <c>math</c>
    /// node has to mean the same thing whichever engine answers it.
    /// </remarks>
    private static object? EvaluateMath(object? left, object? right, MathOperator op)
    {
        if (!TryToDecimal(left, out var leftValue) || !TryToDecimal(right, out var rightValue))
        {
            return null;
        }

        return op switch
        {
            MathOperator.Add => leftValue + rightValue,
            MathOperator.Subtract => leftValue - rightValue,
            MathOperator.Multiply => leftValue * rightValue,
            MathOperator.Divide => rightValue == 0 ? null : leftValue / rightValue,
            _ => throw new ArgumentException($"Unsupported math operator '{op}'.")
        };
    }

    private static bool TryToDecimal(object? value, out decimal result)
    {
        switch (value)
        {
            case decimal number:
                result = number;
                return true;
            case long integer:
                result = integer;
                return true;
            case int integer:
                result = integer;
                return true;
            case double number:
                result = (decimal)number;
                return true;
            case bool flag:
                result = flag ? 1m : 0m;
                return true;
            case string text when decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed):
                result = parsed;
                return true;
            default:
                result = 0m;
                return false;
        }
    }

    private static object? EvaluateDateDiff(
        string unit,
        object? start,
        object? end,
        TimeZoneInfo zone,
        DayOfWeek weekStart)
    {
        if (!TryToWallClock(start, zone, out var startDt) || !TryToWallClock(end, zone, out var endDt))
        {
            return null;
        }

        return RelativeDateResolver.ComputeDateDiff(unit, startDt, endDt, weekStart);
    }

    private static bool TryToWallClock(object? value, TimeZoneInfo zone, out DateTime result)
    {
        switch (value)
        {
            case DateTime dt:
                result = dt.Kind == DateTimeKind.Utc
                    ? TimeZoneInfo.ConvertTimeFromUtc(dt, zone)
                    : dt;
                return true;
            case DateOnly day:
                result = day.ToDateTime(TimeOnly.MinValue);
                return true;
            case DateTimeOffset dto:
                result = TimeZoneInfo.ConvertTime(dto, zone).DateTime;
                return true;
            case string text when DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDay):
                result = parsedDay.ToDateTime(TimeOnly.MinValue);
                return true;
            case string text when DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedInstant):
                result = TimeZoneInfo.ConvertTime(parsedInstant, zone).DateTime;
                return true;
            default:
                result = default;
                return false;
        }
    }

    /// <summary>
    /// Whether a property name is Salesforce's own record metadata rather than a column.
    /// </summary>
    public static bool IsAttributes(string propertyName) =>
        string.Equals(propertyName, AttributesProperty, StringComparison.Ordinal);
}
