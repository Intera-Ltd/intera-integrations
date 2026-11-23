namespace Intera.Salesforce.Api.Infrastructure.Soql;

/// <summary>
/// What kind of SOQL literal a value compared against this field has to be rendered as.
/// </summary>
/// <remarks>
/// SOQL has no bind parameters — a query is a string in a URL — so every value is written into the
/// text, and how it is written depends on the field's type: a string is quoted and escaped, a number
/// and a checkbox are bare, a Date is a bare <c>2026-09-03</c> and a DateTime a bare
/// <c>2026-09-03T10:00:00Z</c>. Quoting a date, or leaving a string unquoted, is a
/// <c>MALFORMED_QUERY</c> from Salesforce; the field list is the only place that knows which is
/// which, so it carries the type.
/// </remarks>
public enum SoqlFieldKind
{
    Text,
    Number,
    Boolean,
    Date,
    DateTime
}

/// <summary>
/// Where a field may appear in a query, beyond simply being selected.
/// </summary>
/// <remarks>
/// Salesforce tracks these as four independent flags per field (<c>filterable</c>, <c>sortable</c>,
/// <c>groupable</c>, <c>aggregatable</c> in <c>/sobjects/{object}/describe</c>), and they genuinely
/// differ: a currency field can be filtered and summed but not grouped, a checkbox can be grouped but
/// not aggregated, a long text area can do none of it. Collapsing them into one "filterable" boolean
/// looked tidy and was wrong — it let <c>GROUP BY Amount</c> through to Salesforce, which answers
/// <c>MALFORMED_QUERY</c> with the whole query echoed back instead of a sentence naming the field.
/// <para>
/// The values here were read from this org's own describe and then confirmed one query at a time
/// against a live org on 2026-09-03; they are metadata of the standard objects, not of one
/// installation.
/// </para>
/// </remarks>
[Flags]
public enum SoqlFieldUse
{
    /// <summary>Selectable only — a long text area.</summary>
    None = 0,

    /// <summary><c>WHERE</c>.</summary>
    Filter = 1,

    /// <summary><c>ORDER BY</c>.</summary>
    Sort = 2,

    /// <summary><c>GROUP BY</c>, and therefore a bare field beside an aggregate.</summary>
    Group = 4,

    /// <summary><c>SUM</c>/<c>AVG</c>/<c>MIN</c>/<c>MAX</c>/<c>COUNT</c>/<c>COUNT_DISTINCT</c>.</summary>
    Aggregate = 8,

    All = Filter | Sort | Group | Aggregate
}

/// <summary>
/// One API-facing field: its snake_case name, the SOQL path it reads, its literal type, and where
/// Salesforce allows it to be used.
/// </summary>
/// <param name="Name">The snake_case name callers use in filters, orders and result expressions.</param>
/// <param name="Path">
/// The SOQL path, either a plain field (<c>StageName</c>) or a parent traversal (<c>Account.Name</c>).
/// A traversal costs nothing extra upstream — SOQL resolves it inside the same query — which is what
/// makes <c>account_name</c> a column here rather than a second request and a join in the dashboard.
/// </param>
/// <param name="Kind">How a value compared against this field is rendered as a SOQL literal.</param>
/// <param name="Uses">
/// Which clauses accept it. Anything outside this set is refused locally, with a 400 naming the field
/// and the clause, rather than left to surface as an upstream <c>MALFORMED_QUERY</c>.
/// </param>
public sealed record SoqlField(string Name, string Path, SoqlFieldKind Kind, SoqlFieldUse Uses = SoqlFieldUse.All)
{
    /// <summary>Text, a picklist, an id or a reference: usable everywhere.</summary>
    public static SoqlField Text(string name, string path) => new(name, path, SoqlFieldKind.Text);

    /// <summary>A whole number. Unlike <see cref="Decimal"/> these <i>are</i> groupable.</summary>
    public static SoqlField Number(string name, string path) => new(name, path, SoqlFieldKind.Number);

    /// <summary>
    /// A currency, double or percent field: filterable, sortable and summable, but <b>not</b>
    /// groupable — Salesforce refuses <c>GROUP BY Amount</c>. Grouping a continuous measure is
    /// rarely what anyone meant anyway; the key is usually the date or the stage next to it.
    /// </summary>
    public static SoqlField Decimal(string name, string path) =>
        new(name, path, SoqlFieldKind.Number, SoqlFieldUse.All & ~SoqlFieldUse.Group);

    /// <summary>A checkbox: groupable, but not aggregatable — there is no <c>SUM</c> of a boolean.</summary>
    public static SoqlField Checkbox(string name, string path) =>
        new(name, path, SoqlFieldKind.Boolean, SoqlFieldUse.All & ~SoqlFieldUse.Aggregate);

    /// <summary>A Date: usable everywhere, and the field to group a time series by.</summary>
    public static SoqlField Day(string name, string path) => new(name, path, SoqlFieldKind.Date);

    /// <summary>
    /// A DateTime: filterable, sortable and aggregatable (<c>MAX(CreatedDate)</c> works), but
    /// <b>not</b> groupable — an instant is unique per row, so Salesforce refuses it as a key.
    /// </summary>
    public static SoqlField Timestamp(string name, string path) =>
        new(name, path, SoqlFieldKind.DateTime, SoqlFieldUse.All & ~SoqlFieldUse.Group);

    /// <summary>A long text area: selectable, and nothing else. Salesforce allows none of the four.</summary>
    public static SoqlField LongText(string name, string path) =>
        new(name, path, SoqlFieldKind.Text, SoqlFieldUse.None);

    /// <summary>Narrows the field to everything but <paramref name="use"/>.</summary>
    public SoqlField Without(SoqlFieldUse use) => this with { Uses = Uses & ~use };
}

/// <summary>
/// One queryable sObject: the SOQL type name plus the ordered field allowlist for it.
/// </summary>
/// <remarks>
/// The list is ordered rather than a bare dictionary because it is also the column order of a
/// passthrough query: SOQL has no <c>SELECT *</c>, so "return the rows as they are" is compiled into
/// this list, and the order it is written in is the order a grid shows.
/// <para>
/// The allowlist is the only thing that decides what a caller may name. Unlike the SQL projects,
/// where it guards a hand-written view, here it also guards the literal rendering: a field's
/// <see cref="SoqlField.Kind"/> is what tells <see cref="SoqlLiteral"/> how to quote a value, so a
/// field that is not in the list has no way to reach the query text at all.
/// </para>
/// </remarks>
public sealed class SoqlObject
{
    private readonly Dictionary<string, SoqlField> lookup;

    public SoqlObject(string objectName, IReadOnlyList<SoqlField> fields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectName);
        ArgumentNullException.ThrowIfNull(fields);

        ObjectName = objectName;
        Fields = fields;
        lookup = fields.ToDictionary(field => field.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The sObject's SOQL type name, e.g. <c>Opportunity</c>.</summary>
    public string ObjectName { get; }

    /// <summary>The allowlist, in the order a passthrough query selects it.</summary>
    public IReadOnlyList<SoqlField> Fields { get; }

    public SoqlField Resolve(string? name)
    {
        var normalized = name?.Trim();

        if (string.IsNullOrWhiteSpace(normalized) || !lookup.TryGetValue(normalized, out var field))
        {
            throw new ArgumentException(
                $"Field '{name}' is not allowed on {ObjectName}. Allowed fields: {string.Join(", ", Fields.Select(item => item.Name).Order(StringComparer.Ordinal))}.");
        }

        return field;
    }

    /// <summary>
    /// Resolves a field that has to survive a particular clause, refusing it — by name, and naming
    /// the clause — when Salesforce does not allow it there.
    /// </summary>
    public SoqlField ResolveFor(string? name, SoqlFieldUse use)
    {
        var field = Resolve(name);

        if (field.Uses.HasFlag(use))
        {
            return field;
        }

        var clause = use switch
        {
            SoqlFieldUse.Filter => "a filter",
            SoqlFieldUse.Sort => "ORDER BY",
            SoqlFieldUse.Group => "GROUP BY",
            SoqlFieldUse.Aggregate => "an aggregate",
            _ => use.ToString()
        };

        var hint = (use, field.Kind) switch
        {
            (SoqlFieldUse.Group, SoqlFieldKind.DateTime) => " Group by a date field instead, or by a field with fewer distinct values.",
            (SoqlFieldUse.Group, SoqlFieldKind.Number) => " Salesforce does not group by a currency or decimal measure; aggregate it instead.",
            (SoqlFieldUse.Aggregate, SoqlFieldKind.Boolean) => " Count the rows and filter on it instead.",
            _ when field.Uses == SoqlFieldUse.None => " It is a long text area: it can be selected, and nothing else.",
            _ => string.Empty
        };

        throw new ArgumentException($"Field '{field.Name}' on {ObjectName} cannot be used in {clause}: Salesforce does not allow it there.{hint}");
    }
}
