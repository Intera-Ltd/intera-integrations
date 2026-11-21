using System.Globalization;
using System.Text.Json;

namespace Intera.Zendesk.Api.Infrastructure;

/// <summary>
/// Reading values out of Zendesk's JSON. Timestamps are ISO 8601 UTC (<c>2026-10-07T09:15:02Z</c>), ids are JSON
/// numbers (up to 64 bits) and are read as text so a lookup and its target always compare equal.
/// </summary>
public static class ZendeskJson
{
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

    public static decimal? Decimal(JsonElement element, string name) =>
        Property(element, name) is { } value ? ToDecimal(value) : null;

    public static decimal? Decimal(JsonElement? element, string name) => element is { } value ? Decimal(value, name) : null;

    public static decimal? ToDecimal(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
        JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null
    };

    public static bool? Boolean(JsonElement element, string name) =>
        Property(element, name) is { } value ? ToBoolean(value) : null;

    public static bool? Boolean(JsonElement? element, string name) => element is { } value ? Boolean(value, name) : null;

    public static bool? ToBoolean(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String when bool.TryParse(value.GetString(), out var flag) => flag,
        _ => null
    };

    /// <summary>A moment (<c>created_at</c>, <c>updated_at</c>), in UTC.</summary>
    public static DateTime? Timestamp(JsonElement element, string name) => ParseTimestamp(String(element, name));

    public static DateTime? Timestamp(JsonElement? element, string name) => element is { } value ? Timestamp(value, name) : null;

    /// <summary>A calendar date (a <c>date</c> custom field), with no time zone attached.</summary>
    public static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        return DateTime.TryParseExact(trimmed[..Math.Min(10, trimmed.Length)], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? DateTime.SpecifyKind(date, DateTimeKind.Unspecified)
            : null;
    }

    public static DateTime? ParseTimestamp(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateTimeOffset.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed.UtcDateTime
            : null;
    }

    /// <summary>Several values as one text column, the way the Jira and Zoho sources join theirs; empty is null.</summary>
    public static string? Join(IEnumerable<string?> values)
    {
        var present = values.Where(static value => !string.IsNullOrEmpty(value)).ToList();
        return present.Count == 0 ? null : string.Join(", ", present);
    }

    /// <summary>A JSON array of scalars as one text column.</summary>
    public static string? JoinArray(JsonElement element, string name) => Join(Array(element, name).Select(Text));

    /// <summary>An object (a user's <c>user_fields</c>) as compact JSON text; null when absent or empty.</summary>
    public static string? ObjectText(JsonElement element, string name) =>
        Object(element, name) is { } value && value.EnumerateObject().Any() ? value.GetRawText() : null;
}
