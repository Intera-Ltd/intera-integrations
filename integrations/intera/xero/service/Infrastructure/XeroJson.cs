using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Intera.Xero.Api.Infrastructure;

/// <summary>
/// Reading values out of Xero's JSON. Two things are Xero's own: dates arrive in the old Microsoft form
/// <c>/Date(1539993600000+0000)/</c> (milliseconds since the epoch, UTC, plus the offset they were entered in), and
/// report cells are text — <c>"1234.00"</c>, <c>"2018-05-13 00:00:00"</c>, <c>""</c> for nothing.
/// </summary>
public static partial class XeroJson
{
    private static readonly string[] ReportDateFormats =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd",
        "yyyy-MM"
    ];

    public static JsonElement? Object(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    public static IEnumerable<JsonElement> Array(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];

    public static IEnumerable<JsonElement> Array(JsonElement? element, string name) =>
        element is { } value ? Array(value, name) : [];

    /// <summary>
    /// The first record under <paramref name="name"/>, whether Xero sent the usual one-element array or, as its
    /// own OpenAPI example for <c>GET /Budgets/{id}</c> does, a bare object.
    /// </summary>
    public static JsonElement? First(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Object => value,
            JsonValueKind.Array => value.EnumerateArray().Where(static item => item.ValueKind == JsonValueKind.Object).Cast<JsonElement?>().FirstOrDefault(),
            _ => null
        };
    }

    public static string? String(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() is { Length: > 0 } text ? text : null,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    public static string? String(JsonElement? element, string name) => element is { } value ? String(value, name) : null;

    public static decimal? Decimal(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.String => ParseDecimal(value.GetString()),
            _ => null
        };
    }

    public static decimal? Decimal(JsonElement? element, string name) => element is { } value ? Decimal(value, name) : null;

    public static int? Integer(JsonElement element, string name) =>
        Decimal(element, name) is { } number && number == Math.Truncate(number) ? (int)number : null;

    public static bool? Boolean(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var flag) => flag,
            _ => null
        };
    }

    public static bool? Boolean(JsonElement? element, string name) => element is { } value ? Boolean(value, name) : null;

    /// <summary>A calendar date (an invoice date, a due date, a lock date), with no time zone attached.</summary>
    public static DateTime? Date(JsonElement element, string name) => ParseDate(String(element, name));

    /// <summary>A moment, in UTC (<c>UpdatedDateUTC</c>, <c>CreatedDateUTC</c>).</summary>
    public static DateTime? Timestamp(JsonElement element, string name) => ParseTimestamp(String(element, name));

    /// <summary>
    /// A date-only value. In the Microsoft form the milliseconds are the UTC instant and the offset is the zone it
    /// was entered in, so the calendar date is the instant moved by that offset — not the UTC date, which for an
    /// organisation east of Greenwich would be the day before.
    /// </summary>
    public static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (TryParseMicrosoft(text, out var instant, out var offset))
        {
            return DateTime.SpecifyKind(instant.ToOffset(offset).Date, DateTimeKind.Unspecified);
        }

        return TryParseText(text, out var parsed) ? DateTime.SpecifyKind(parsed.Date, DateTimeKind.Unspecified) : null;
    }

    public static DateTime? ParseTimestamp(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (TryParseMicrosoft(text, out var instant, out _))
        {
            return instant.UtcDateTime;
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed.UtcDateTime
            : null;
    }

    /// <summary>A report cell's amount: <c>"1234.00"</c>, <c>"-1,850.00"</c>, <c>"(12.50)"</c>; empty is null.</summary>
    public static decimal? ParseDecimal(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return decimal.TryParse(
            text.Trim(),
            NumberStyles.Number | NumberStyles.AllowParentheses,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private static bool TryParseText(string text, out DateTime value) =>
        DateTime.TryParseExact(text.Trim(), ReportDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value)
        || DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    private static bool TryParseMicrosoft(string text, out DateTimeOffset instant, out TimeSpan offset)
    {
        var match = MicrosoftDate().Match(text);
        if (!match.Success || !long.TryParse(match.Groups["ms"].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var milliseconds))
        {
            instant = default;
            offset = default;
            return false;
        }

        instant = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        offset = TimeSpan.Zero;

        if (match.Groups["offset"].Success)
        {
            var raw = match.Groups["offset"].Value;
            var hours = int.Parse(raw.AsSpan(1, 2), CultureInfo.InvariantCulture);
            var minutes = int.Parse(raw.AsSpan(3, 2), CultureInfo.InvariantCulture);
            offset = new TimeSpan(hours, minutes, 0);
            if (raw[0] == '-')
            {
                offset = -offset;
            }
        }

        return true;
    }

    [GeneratedRegex(@"^/Date\((?<ms>-?\d+)(?<offset>[+-]\d{4})?\)/$", RegexOptions.CultureInvariant)]
    private static partial Regex MicrosoftDate();
}
