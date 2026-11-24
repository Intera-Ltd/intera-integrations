using System.Globalization;
using System.Text.Json;

namespace Intera.Soft1.Api.Infrastructure;

/// <summary>
/// Reads the values Soft1 sends into the types a row carries.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything arrives as text first.</b> A browser row is an array of strings in the documented examples
/// (<c>"CUSTOMER;47", "100", "Soft One Technologies S.A."</c>), including its numbers, and a browser is a
/// presentation of data — so a number may come formatted for the web account's locale. Greek formatting writes
/// <c>1.234,56</c>. <see cref="Decimal"/> therefore takes the last of <c>.</c>/<c>,</c> as the decimal separator
/// and drops the other; a single separator is decimal whichever it is. The one reading that is then wrong is an
/// invariant-culture thousands group without decimals (<c>1,234</c> meaning 1234), which JSON never produces.
/// </para>
/// <para>
/// This is written from the documentation without a live installation; the first thing to check on real data
/// is what a browser actually sends for an amount and a date.
/// </para>
/// </remarks>
public static class Soft1Values
{
    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.fff",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss.fff",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd",
        "dd/MM/yyyy HH:mm:ss",
        "dd/MM/yyyy HH:mm",
        "dd/MM/yyyy",
        "d/M/yyyy",
        "dd.MM.yyyy",
        "dd-MM-yyyy"
    ];

    /// <summary>A JSON value as text: strings as they are, numbers and booleans as their JSON text, null and blanks as null.</summary>
    public static string? Text(JsonElement value)
    {
        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "1",
            JsonValueKind.False => "0",
            _ => null
        };

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    public static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static decimal? Decimal(string? value)
    {
        var text = Text(value);
        if (text is null)
        {
            return null;
        }

        var lastDot = text.LastIndexOf('.');
        var lastComma = text.LastIndexOf(',');

        string normalized;
        if (lastDot >= 0 && lastComma >= 0)
        {
            // Both present: the later one is the decimal separator, the other groups thousands.
            normalized = lastComma > lastDot
                ? text.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.')
                : text.Replace(",", string.Empty, StringComparison.Ordinal);
        }
        else
        {
            normalized = text.Replace(',', '.');
        }

        normalized = normalized.Replace(" ", string.Empty, StringComparison.Ordinal);

        return decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    public static int? Integer(string? value)
    {
        var number = Decimal(value);
        return number is { } n && n == decimal.Truncate(n) && n >= int.MinValue && n <= int.MaxValue ? (int)n : null;
    }

    /// <summary>
    /// A date or timestamp as a zone-less <see cref="DateTime"/> — Soft1 stores the installation's local time and
    /// sends no offset, so none is invented.
    /// </summary>
    public static DateTime? Date(string? value)
    {
        var text = Text(value);
        if (text is null)
        {
            return null;
        }

        return DateTime.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            || DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)
                ? parsed
                : null;
    }

    /// <summary>Soft1 flags are 1/0 (ISACTIVE, ISCANCEL …); text forms are accepted too.</summary>
    public static bool? Flag(string? value)
    {
        var text = Text(value);
        return text?.ToUpperInvariant() switch
        {
            null => null,
            "1" or "TRUE" or "Y" or "YES" or "-1" => true,
            "0" or "FALSE" or "N" or "NO" => false,
            _ => Decimal(text) is { } number ? number != 0 : null
        };
    }
}
