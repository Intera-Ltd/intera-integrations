using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Intera.Unleashed.Api.Infrastructure.Json;

/// <summary>
/// Reads Unleashed's dates, which are written in the old WCF/ASP.NET form
/// <c>/Date(1459218740803)/</c> rather than as ISO-8601.
/// </summary>
/// <remarks>
/// <para>
/// This is not a stylistic difference — <c>System.Text.Json</c> cannot read that form at all, so
/// without this converter every date on every Unleashed object throws while deserializing. It is
/// applied per property (<c>[JsonConverter]</c>) rather than through the serializer options, because
/// the shared <c>JsonHttpClient</c> owns the options and they are the same for all ten services.
/// </para>
/// <para>
/// The number is milliseconds since the Unix epoch <b>in UTC</b>. A trailing offset
/// (<c>/Date(1459218740803+1300)/</c>) is a display hint about the writer's zone and does not move
/// the instant, so it is read and discarded — adding it would shift every timestamp by the offset.
/// ISO-8601 is accepted too: not every Unleashed endpoint has been observed, and a plain string date
/// is the other spelling a JSON API plausibly uses.
/// </para>
/// <para>
/// Everything is returned as UTC. Unleashed's <c>modifiedSince</c> parameter is documented as UTC and
/// its date-only values (an order date, a delivery date) come back as UTC midnight, so treating the
/// stream as UTC keeps a calendar day the same day. A relative-date filter is then resolved against
/// <c>QueryRequest.TimeZone</c> by the query engine, exactly as everywhere else here.
/// </para>
/// </remarks>
public sealed class UnleashedDateTimeConverter : JsonConverter<DateTime?>
{
    private const string Prefix = "/Date(";
    private const string Suffix = ")/";

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.String:
                return Parse(reader.GetString());

            case JsonTokenType.Number:
                // Not observed in the wild, but a bare epoch is cheap to accept and impossible to
                // misread: it can only be one thing.
                return reader.TryGetInt64(out var epoch)
                    ? DateTimeOffset.FromUnixTimeMilliseconds(epoch).UtcDateTime
                    : null;

            default:
                throw new JsonException($"Unexpected token '{reader.TokenType}' where an Unleashed date was expected.");
        }
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value.Value);
        }
    }

    /// <summary>
    /// Reads either spelling. An unreadable value becomes <c>null</c> rather than an exception: one
    /// unparseable timestamp should cost that column on that row, not the whole query.
    /// </summary>
    public static DateTime? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Trim();

        if (text.StartsWith(Prefix, StringComparison.Ordinal) && text.EndsWith(Suffix, StringComparison.Ordinal))
        {
            var body = text[Prefix.Length..^Suffix.Length];

            // A trailing "+1300"/"-0500" is the writer's local offset. The epoch in front of it is
            // already UTC, so the offset is split off and dropped rather than applied.
            var offsetIndex = body.LastIndexOfAny(['+', '-']);
            if (offsetIndex > 0)
            {
                body = body[..offsetIndex];
            }

            return long.TryParse(body, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch)
                ? DateTimeOffset.FromUnixTimeMilliseconds(epoch).UtcDateTime
                : null;
        }

        return DateTime.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
    }
}
