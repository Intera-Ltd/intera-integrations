using System.Globalization;
using System.Text.Json;

namespace Intera.Jira.Api.Infrastructure;

/// <summary>
/// Reading values out of Jira's JSON. Jira's timestamps are ISO 8601 with an offset (<c>2026-10-07T09:15:02.123+0300</c>
/// — note the offset without a colon), dates are <c>yyyy-MM-dd</c>, ids are strings on the platform API and numbers on
/// the Agile API; every id is read as text here so a lookup and its target always compare equal.
/// </summary>
public static class JiraJson
{
    private static readonly string[] TimestampFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss.fffzzz",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.fffK",
        "yyyy-MM-dd'T'HH:mm:ssK"
    ];

    public static JsonElement? Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? value
            : null;

    public static JsonElement? Property(JsonElement? element, string name) =>
        element is { } value ? Property(value, name) : null;

    public static JsonElement? Object(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.Object } value ? value : null;

    public static JsonElement? Object(JsonElement? element, string name) =>
        element is { } value ? Object(value, name) : null;

    public static IEnumerable<JsonElement> Array(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.Array } value ? value.EnumerateArray() : [];

    public static IEnumerable<JsonElement> Array(JsonElement? element, string name) =>
        element is { } value ? Array(value, name) : [];

    /// <summary>Text of a scalar: a string as is (empty is null), a number or boolean as its JSON text.</summary>
    public static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() is { Length: > 0 } text ? text : null,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null
    };

    public static string? String(JsonElement element, string name) =>
        Property(element, name) is { } value ? Text(value) : null;

    public static string? String(JsonElement? element, string name) =>
        element is { } value ? String(value, name) : null;

    public static decimal? Decimal(JsonElement element, string name)
    {
        if (Property(element, name) is not { } value)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    public static decimal? Decimal(JsonElement? element, string name) => element is { } value ? Decimal(value, name) : null;

    public static long? Long(JsonElement element, string name) =>
        Decimal(element, name) is { } number && number == Math.Truncate(number) ? (long)number : null;

    public static long? Long(JsonElement? element, string name) => element is { } value ? Long(value, name) : null;

    public static bool? Boolean(JsonElement element, string name)
    {
        if (Property(element, name) is not { } value)
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

    /// <summary>A moment (<c>created</c>, <c>updated</c>, <c>started</c>), in UTC.</summary>
    /// <summary>
    /// An instant, as Jira writes it: ISO text with a <c>+0300</c>-style offset almost everywhere, but epoch
    /// milliseconds in <c>changelog/bulkfetch</c>'s <c>created</c> (seen on a live site 2026-10-07).
    /// </summary>
    public static DateTime? Timestamp(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out var milliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime
            : ParseTimestamp(String(element, name));

    public static DateTime? Timestamp(JsonElement? element, string name) => element is { } value ? Timestamp(value, name) : null;

    /// <summary>A calendar date (<c>duedate</c>, a version's <c>releaseDate</c>), with no time zone attached.</summary>
    public static DateTime? Date(JsonElement element, string name) => ParseDate(String(element, name));

    public static DateTime? Date(JsonElement? element, string name) => element is { } value ? Date(value, name) : null;

    public static DateTime? ParseTimestamp(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Jira writes the offset as +0300; the .NET offset specifiers want +03:00.
        var trimmed = text.Trim();
        if (trimmed.Length > 5
            && trimmed[^5] is '+' or '-'
            && char.IsAsciiDigit(trimmed[^4]) && char.IsAsciiDigit(trimmed[^3])
            && char.IsAsciiDigit(trimmed[^2]) && char.IsAsciiDigit(trimmed[^1]))
        {
            trimmed = $"{trimmed[..^2]}:{trimmed[^2..]}";
        }

        if (DateTimeOffset.TryParseExact(trimmed, TimestampFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var exact))
        {
            return exact.UtcDateTime;
        }

        return DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed.UtcDateTime
            : null;
    }

    public static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateTime.TryParseExact(text.Trim()[..Math.Min(10, text.Trim().Length)], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? DateTime.SpecifyKind(date, DateTimeKind.Unspecified)
            : null;
    }

    /// <summary>Several values joined as one comma-separated text column; empty is null.</summary>
    public static string? Join(IEnumerable<string?> values)
    {
        var present = values.Where(static value => !string.IsNullOrEmpty(value)).ToList();
        return present.Count == 0 ? null : string.Join(", ", present);
    }
}
