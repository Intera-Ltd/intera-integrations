using Intera.Common.Infrastructure.Query;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Intera.ZohoInventory.Api.Infrastructure;

/// <summary>One custom field as <c>/api/Schema</c> reports it.</summary>
/// <param name="Name">Row key, e.g. <c>cf_vessel_name</c> or <c>item_cf_serial_batch</c>.</param>
/// <param name="Label">The label the field carries in Zoho.</param>
/// <param name="Type">Descriptor type for the plugin: <c>string</c>, <c>number</c>, <c>date</c> or <c>boolean</c>.</param>
public sealed record ZohoCustomFieldDescriptor(string Name, string Label, string Type);

/// <summary>
/// Turns Zoho's <c>custom_fields</c> arrays into row columns, and lets those columns through the
/// allowlist.
/// </summary>
/// <remarks>
/// <para>
/// <b>Custom fields are exposed dynamically.</b> One deployment serves one Zoho organization, and every
/// record carries its filled-in custom fields in the response it came in — label, value and type —
/// so reading a row's values needs no describe call and no static configuration (the field list for the
/// admin's picker comes from the organization's settings, see <c>SchemaService</c>).
/// </para>
/// <para>
/// <b>Naming.</b> A document's own custom fields become <c>cf_&lt;name&gt;</c>, a line's become
/// <c>item_cf_&lt;name&gt;</c>, and a line row carries both. The name is Zoho's <c>api_name</c> when
/// the response has one (Zoho already spells it <c>cf_…</c>), otherwise the label reduced to
/// <c>[a-z0-9_]</c>, otherwise the field id. The prefix is what keeps a custom field called "Status"
/// from ever shadowing the built-in <c>status</c>.
/// </para>
/// <para>
/// <b>The allowlist accepts any well-formed <c>cf_</c>/<c>item_cf_</c> name</b> (see
/// <see cref="Allow"/>), reading null where a row has none. That is safe in a way it would not be on
/// the SQL path — nothing is compiled into a query, the name is only ever a dictionary key — and it is
/// necessary: Zoho leaves an <b>empty</b> custom field out of a record's array altogether, so the set
/// of <c>cf_</c> keys differs from row to row and no row set is guaranteed to name them all.
/// </para>
/// </remarks>
public static class ZohoCustomFields
{
    public const string DocumentPrefix = "cf_";
    public const string LinePrefix = "item_cf_";

    /// <summary>Writes a <c>custom_fields</c>-style array onto <paramref name="row"/>.</summary>
    public static void AddTo(Dictionary<string, object?> row, JsonElement owner, string arrayName, string prefix)
    {
        ArgumentNullException.ThrowIfNull(row);

        foreach (var field in ZohoJson.Array(owner, arrayName))
        {
            if (Describe(field, prefix) is { } descriptor)
            {
                row[descriptor.Name] = ReadValue(field, descriptor.Type);
            }
        }
    }

    /// <summary>Descriptors for every custom field in the array; used by schema discovery.</summary>
    public static IEnumerable<ZohoCustomFieldDescriptor> Describe(JsonElement owner, string arrayName, string prefix)
    {
        foreach (var field in ZohoJson.Array(owner, arrayName))
        {
            if (Describe(field, prefix) is { } descriptor)
            {
                yield return descriptor;
            }
        }
    }

    /// <summary>Wraps a static allowlist so it also admits custom-field columns.</summary>
    public static IReadOnlyDictionary<string, InMemoryQueryField> Allow(
        IReadOnlyDictionary<string, InMemoryQueryField> fields,
        params string[] prefixes)
    {
        return new CustomFieldAllowlist(fields, prefixes);
    }

    private static ZohoCustomFieldDescriptor? Describe(JsonElement field, string prefix)
    {
        var label = ZohoJson.Text(field, "label");
        var baseName = Normalize(ZohoJson.Text(field, "api_name"))
            ?? Normalize(label)
            ?? Normalize(ZohoJson.FirstText(field, "customfield_id", "field_id"));

        if (baseName is null)
        {
            return null;
        }

        // Zoho's api_name already starts with cf_; a label or an id does not.
        if (baseName.StartsWith(DocumentPrefix, StringComparison.Ordinal))
        {
            baseName = baseName[DocumentPrefix.Length..];
        }

        if (baseName.Length == 0)
        {
            return null;
        }

        return new ZohoCustomFieldDescriptor(prefix + baseName, label ?? baseName, TypeOf(field));
    }

    /// <summary>
    /// Maps Zoho's <c>data_type</c> onto a descriptor type. An unknown or absent type stays text:
    /// comparing a number as text orders "10" before "9", but comparing text as a number drops every
    /// row that is not one, and only the second loses data.
    /// </summary>
    private static string TypeOf(JsonElement field)
    {
        return ZohoJson.Text(field, "data_type")?.ToLowerInvariant() switch
        {
            "number" or "decimal" or "amount" or "percent" or "autonumber_numeric" => "number",
            "date" or "datetime" or "date_time" => "date",
            "check_box" or "checkbox" or "boolean" => "boolean",
            _ => "string"
        };
    }

    private static object? ReadValue(JsonElement field, string type)
    {
        if (ZohoJson.Property(field, "value") is not { } value)
        {
            return null;
        }

        return type switch
        {
            "number" => ZohoJson.Number(value),
            "boolean" => ZohoJson.Bool(value),
            // Boxed separately on purpose: as one conditional expression the DateTime would convert
            // implicitly to DateTimeOffset and pick up the container's offset, moving a date-only
            // value off its calendar day for anyone reading it in another zone.
            "date" when ZohoJson.Text(value) is { } text && text.Length > 10 => ZohoJson.ParseTimestamp(text),
            "date" when ZohoJson.Text(value) is { } text => ZohoJson.ParseDate(text),
            "date" => null,
            _ when value.ValueKind == JsonValueKind.Array => JoinArray(value),
            _ => ZohoJson.Text(value)
        };
    }

    /// <summary>A multi-select value, as the comma-joined text Zoho itself displays.</summary>
    private static string? JoinArray(JsonElement value)
    {
        var parts = value.EnumerateArray().Select(ZohoJson.Text).Where(static part => part is not null).ToArray();
        return parts.Length == 0 ? null : string.Join(", ", parts);
    }

    private static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var builder = new StringBuilder(text.Length);
        var pendingSeparator = false;

        foreach (var character in text.Trim().ToLowerInvariant())
        {
            if (character is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                if (pendingSeparator && builder.Length > 0)
                {
                    builder.Append('_');
                }

                builder.Append(character);
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>
    /// A read-only view over the static allowlist that also resolves any <c>cf_…</c>/<c>item_cf_…</c>
    /// name. <c>InMemoryQueryEngine</c> only ever calls <see cref="TryGetValue"/> on it, and
    /// <see cref="Keys"/> for the "allowed fields" error message.
    /// </summary>
    private sealed class CustomFieldAllowlist(
        IReadOnlyDictionary<string, InMemoryQueryField> fields,
        string[] prefixes) : IReadOnlyDictionary<string, InMemoryQueryField>
    {
        public InMemoryQueryField this[string key] =>
            TryGetValue(key, out var field) ? field : throw new KeyNotFoundException(key);

        public IEnumerable<string> Keys => fields.Keys.Concat(prefixes.Select(static prefix => prefix + "*"));

        public IEnumerable<InMemoryQueryField> Values => fields.Values;

        public int Count => fields.Count;

        public bool ContainsKey(string key) => TryGetValue(key, out _);

        public bool TryGetValue(string key, [MaybeNullWhen(false)] out InMemoryQueryField value)
        {
            if (fields.TryGetValue(key, out value))
            {
                return true;
            }

            var name = key.ToLower(CultureInfo.InvariantCulture);
            if (IsCustomFieldName(name))
            {
                value = new InMemoryQueryField(name, row => row.TryGetValue(name, out var cell) ? cell : null);
                return true;
            }

            value = null;
            return false;
        }

        public IEnumerator<KeyValuePair<string, InMemoryQueryField>> GetEnumerator() => fields.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        private bool IsCustomFieldName(string name)
        {
            foreach (var prefix in prefixes)
            {
                if (name.Length > prefix.Length
                    && name.StartsWith(prefix, StringComparison.Ordinal)
                    && name.AsSpan(prefix.Length).IndexOfAnyExcept("abcdefghijklmnopqrstuvwxyz0123456789_") < 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
