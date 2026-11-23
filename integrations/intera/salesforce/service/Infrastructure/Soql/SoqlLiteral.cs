using System.Globalization;
using System.Text;

namespace Intera.Salesforce.Api.Infrastructure.Soql;

/// <summary>
/// Renders a request value as a SOQL literal.
/// </summary>
/// <remarks>
/// This is the security boundary of the whole project, and it is a different one from the rest of the
/// solution's. <c>QuerySqlBuilder</c> never writes a value into its SQL — every one becomes a
/// <c>@p0</c> bound by <c>Microsoft.Data.SqlClient</c>. SOQL over REST has no equivalent: the query
/// is a string in a URL, so a value has to be written into the text and escaped here. Two things
/// therefore carry the weight:
/// <list type="bullet">
/// <item>The field allowlist decides what may be compared at all, so a caller cannot name a field —
/// or an object — the endpoint did not offer.</item>
/// <item>Every value passes through this class, which quotes and escapes text and refuses anything
/// that is not the field's own type.</item>
/// </list>
/// </remarks>
public static class SoqlLiteral
{
    /// <summary>
    /// Renders <paramref name="value"/> for a comparison against <paramref name="field"/>.
    /// </summary>
    public static string Render(SoqlField field, object? value, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(zone);

        if (value is null)
        {
            // SOQL spells the null comparison "= null" / "!= null" — there is no IS NULL — so a null
            // literal is legal in exactly the places the DSL already allows one.
            return "null";
        }

        return field.Kind switch
        {
            SoqlFieldKind.Text => RenderString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty),
            SoqlFieldKind.Number => RenderNumber(field, value),
            SoqlFieldKind.Boolean => RenderBoolean(field, value),
            SoqlFieldKind.Date => RenderDate(field, value, zone),
            SoqlFieldKind.DateTime => RenderDateTime(field, value, zone),
            _ => throw new ArgumentException($"Field '{field.Name}' has an unsupported type.")
        };
    }

    /// <summary>
    /// Quotes and escapes a string for a SOQL literal.
    /// </summary>
    /// <remarks>
    /// The reserved set is Salesforce's own for string literals. The single quote and the backslash
    /// are what closes or continues a literal, and the control characters are escaped because SOQL
    /// reads their two-character forms — leaving a raw newline in the text is a parse error rather
    /// than a value.
    /// </remarks>
    public static string RenderString(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('\'');

        foreach (var character in value)
        {
            switch (character)
            {
                case '\\': builder.Append("\\\\"); break;
                case '\'': builder.Append("\\'"); break;
                case '"': builder.Append("\\\""); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                default: builder.Append(character); break;
            }
        }

        builder.Append('\'');
        return builder.ToString();
    }

    /// <summary>
    /// Quotes and escapes a LIKE pattern, leaving <c>%</c> and <c>_</c> as the wildcards they are.
    /// </summary>
    /// <remarks>
    /// The host builds the pattern (<c>%text%</c>), so the wildcards are the caller's intent and must
    /// survive; only the literal-terminating characters are escaped. A backslash in the search text
    /// becomes a doubled one, which SOQL reads as a single literal backslash — the same meaning it
    /// had.
    /// <para>
    /// SOQL's LIKE is case-insensitive by definition, which is the behaviour
    /// <c>QuerySqlBuilder.BuildLikeCondition</c> has to force with <c>LOWER()</c> against a
    /// case-sensitive collation. Nothing extra is needed here.
    /// </para>
    /// </remarks>
    public static string RenderLikePattern(object value)
    {
        return RenderString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
    }

    private static string RenderNumber(SoqlField field, object value)
    {
        if (value is bool)
        {
            throw new ArgumentException($"Field '{field.Name}' is numeric and cannot be compared to a boolean.");
        }

        try
        {
            return Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            throw new ArgumentException($"Field '{field.Name}' is numeric and '{value}' is not a number.", exception);
        }
    }

    private static string RenderBoolean(SoqlField field, object value)
    {
        return value switch
        {
            bool boolean => boolean ? "true" : "false",
            string text when bool.TryParse(text, out var parsed) => parsed ? "true" : "false",
            _ => throw new ArgumentException($"Field '{field.Name}' is a checkbox and '{value}' is not true or false.")
        };
    }

    private static string RenderDate(SoqlField field, object value, TimeZoneInfo zone)
    {
        var instant = ResolveInstant(field, value, zone);

        // A Date field in Salesforce carries no time and no zone, so the day is taken as it was
        // written in the query's zone rather than converted to UTC first — converting would move
        // "today" to yesterday for any caller east of UTC late in the day.
        return instant.Local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string RenderDateTime(SoqlField field, object value, TimeZoneInfo zone)
    {
        var instant = ResolveInstant(field, value, zone);

        // Always UTC with a literal Z. SOQL accepts an offset form too, but pinning one spelling
        // means the query text says exactly which instant it means, whatever the org's or the
        // container's zone happens to be.
        return instant.Utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Resolves a value to both the wall-clock reading in the query's zone and the matching UTC
    /// instant. A value that already carries an offset keeps it; one that does not is read as wall
    /// clock in the query's zone, which is the same rule <c>RelativeDateResolver</c> follows.
    /// </summary>
    private static (DateTime Local, DateTime Utc) ResolveInstant(SoqlField field, object value, TimeZoneInfo zone)
    {
        switch (value)
        {
            case DateTimeOffset offset:
                return (TimeZoneInfo.ConvertTime(offset, zone).DateTime, offset.UtcDateTime);

            case DateTime dateTime:
                return FromDateTime(dateTime, zone);

            case string text when CarriesOffset(text) && DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsedOffset):
                return (TimeZoneInfo.ConvertTime(parsedOffset, zone).DateTime, parsedOffset.UtcDateTime);

            case string text when DateTime.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed):
                return FromDateTime(parsed, zone);

            default:
                throw new ArgumentException($"Field '{field.Name}' is a date and '{value}' is not one.");
        }
    }

    private static (DateTime Local, DateTime Utc) FromDateTime(DateTime value, TimeZoneInfo zone)
    {
        if (value.Kind == DateTimeKind.Utc)
        {
            return (TimeZoneInfo.ConvertTimeFromUtc(value, zone), value);
        }

        var local = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

        // GetUtcOffset rather than ConvertTimeToUtc: the latter throws on an instant inside a
        // spring-forward gap, and a filter boundary that lands on one is a query to answer, not to
        // reject.
        var offset = zone.GetUtcOffset(local);
        return (local, new DateTimeOffset(local, offset).UtcDateTime);
    }

    /// <summary>
    /// Whether the text spelled out a zone, as opposed to being a bare local reading that
    /// <see cref="DateTimeOffset.TryParse(string, IFormatProvider, DateTimeStyles, out DateTimeOffset)"/>
    /// would silently stamp with the machine's own offset.
    /// </summary>
    private static bool CarriesOffset(string text)
    {
        var trimmed = text.AsSpan().Trim();
        if (trimmed.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // A sign after the time separator is a zone offset, not one of the date's own separators.
        var timeSeparator = trimmed.IndexOf(':');
        if (timeSeparator < 0)
        {
            return false;
        }

        var tail = trimmed[timeSeparator..];
        return tail.Contains('+') || tail.Contains('-');
    }
}
