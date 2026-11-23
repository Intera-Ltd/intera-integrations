using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Intera.ZohoInventory.Api.Infrastructure;

/// <summary>
/// Reads Zoho values by what they are rather than by what the documentation says they are.
/// </summary>
/// <remarks>
/// <para>
/// Every reader returns null for a missing property, a JSON null and an empty string alike. Zoho
/// writes <c>""</c> where another API would leave a field out (an unset <c>location_name</c>, a blank
/// <c>reference_number</c>), and a dashboard filtering <c>IS_NULL</c> should not have to know which of
/// the two a given field happens to use.
/// </para>
/// <para>
/// <b>Dates come in two unrelated spellings.</b> A document date is a bare <c>yyyy-MM-dd</c> in the
/// organization's own calendar, and is emitted as a <see cref="DateTime"/> with no zone — shifting it
/// into UTC would move an order dated the 1st to the 31st for anyone west of Greenwich. A timestamp
/// (<c>created_time</c>, <c>last_modified_time</c>) carries an offset, written either
/// <c>2015-05-28 00:00:00+00:00</c> or <c>2024-06-15T10:30:00.000+0530</c> depending on the
/// endpoint, and is emitted as a <see cref="DateTimeOffset"/> so the instant survives.
/// </para>
/// </remarks>
public static partial class ZohoJson
{
    private static readonly string[] DateTimeFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss.fffzzz",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd HH:mm:sszzz",
        "yyyy-MM-dd HH:mm:ss.fffzzz"
    ];

    public static JsonElement? Property(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                ? value
                : null;
    }

    /// <summary>The first of <paramref name="path"/>'s properties, nested left to right.</summary>
    public static JsonElement? Path(JsonElement element, params ReadOnlySpan<string> path)
    {
        var current = element;
        foreach (var name in path)
        {
            if (Property(current, name) is not { } next)
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    public static IEnumerable<JsonElement> Array(JsonElement element, string name)
    {
        return Property(element, name) is { ValueKind: JsonValueKind.Array } array
            ? array.EnumerateArray().Where(static item => item.ValueKind == JsonValueKind.Object)
            : [];
    }

    public static string? Text(JsonElement element, string name)
    {
        return Property(element, name) is { } value ? Text(value) : null;
    }

    /// <summary>The first of several spellings that carries a value.</summary>
    public static string? FirstText(JsonElement element, params ReadOnlySpan<string> names)
    {
        foreach (var name in names)
        {
            if (Text(element, name) is { } text)
            {
                return text;
            }
        }

        return null;
    }

    public static string? Text(JsonElement value)
    {
        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };

        text = text?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    public static decimal? Number(JsonElement element, string name)
    {
        return Property(element, name) is { } value ? Number(value) : null;
    }

    public static decimal? Number(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            // Zoho writes some quantities as strings ("total_quantity": "1"), and a formatted amount
            // may carry thousands separators; only the separators are tolerated, never a currency sign.
            JsonValueKind.String when decimal.TryParse(
                value.GetString()?.Replace(",", string.Empty, StringComparison.Ordinal),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => null
        };
    }

    public static bool? Bool(JsonElement element, string name)
    {
        return Property(element, name) is { } value ? Bool(value) : null;
    }

    public static bool? Bool(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            JsonValueKind.Number when value.TryGetInt32(out var number) => number != 0,
            _ => null
        };
    }

    /// <summary>A calendar date, kept in the organization's own calendar. See the class remarks.</summary>
    public static DateTime? Date(JsonElement element, string name)
    {
        return Text(element, name) is { } text ? ParseDate(text) : null;
    }

    public static DateTime? ParseDate(string text)
    {
        // A date field occasionally carries a timestamp; its calendar part is the date.
        var datePart = text.Length >= 10 ? text[..10] : text;

        return DateTime.TryParseExact(datePart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    /// <summary>An instant with its offset. See the class remarks.</summary>
    public static DateTimeOffset? Timestamp(JsonElement element, string name)
    {
        return Text(element, name) is { } text ? ParseTimestamp(text) : null;
    }

    public static DateTimeOffset? ParseTimestamp(string text)
    {
        // "+0530" → "+05:30": the colon-less offset is what .NET's zzz will not read.
        var normalized = CompactOffset().Replace(text, "$1:$2");

        if (DateTimeOffset.TryParseExact(normalized, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return exact;
        }

        // A bare date where a timestamp was documented (contacts' created_time is one): midnight UTC is
        // the only reading that does not invent an offset.
        return ParseDate(text) is { } date
            ? new DateTimeOffset(date, TimeSpan.Zero)
            : null;
    }

    [GeneratedRegex(@"([+-]\d{2})(\d{2})$")]
    private static partial Regex CompactOffset();
}
