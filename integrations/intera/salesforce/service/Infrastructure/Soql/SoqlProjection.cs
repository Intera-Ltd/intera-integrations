using Intera.Common.Models.Query;

namespace Intera.Salesforce.Api.Infrastructure.Soql;

/// <summary>
/// How one output column is read back out of a Salesforce record.
/// </summary>
/// <remarks>
/// Salesforce does not answer with the caller's aliases. In a plain query it returns each field under
/// its own path, nested for a parent traversal (<c>{"Account":{"Name":"…"}}</c>); in an aggregate
/// query it returns each expression under the SOQL alias the query gave it. This tree is the mapping
/// back, and it is built by the query builder at the same moment it writes the SELECT — so the two
/// cannot drift.
/// </remarks>
public abstract record SoqlValueSource;

/// <summary>
/// Read a field by its SOQL path, walking the nesting a parent traversal produces.
/// </summary>
/// <param name="Path">The SOQL path, e.g. <c>Account.Name</c>.</param>
/// <param name="Kind">
/// The field's type, carried so the reader can normalise Salesforce's own spellings: a Date arrives
/// as <c>2026-09-03</c> and a DateTime as <c>2026-09-03T10:00:00.000+0000</c>, and both are turned
/// into the CLR types this solution's JSON already emits everywhere else.
/// </param>
public sealed record SoqlPathValue(string Path, SoqlFieldKind Kind) : SoqlValueSource;

/// <summary>Read an aggregate by the generated SOQL alias the SELECT gave it.</summary>
public sealed record SoqlAliasValue(string SoqlAlias) : SoqlValueSource;

/// <summary>
/// The <c>exists</c> aggregate: SOQL has no such function, so the SELECT asks for a COUNT and this
/// turns it into the boolean the DSL promises — the same value
/// <c>InMemoryQueryEngine</c> and <c>QuerySqlBuilder</c> produce for the same node.
/// </summary>
public sealed record SoqlExistsValue(string SoqlAlias) : SoqlValueSource;

/// <summary>A literal from the request, echoed back untouched.</summary>
public sealed record SoqlLiteralValue(object? Value) : SoqlValueSource;

/// <summary>
/// Arithmetic over two values already present in the same answered row.
/// </summary>
/// <remarks>
/// SOQL cannot do arithmetic in a SELECT, so a <c>math</c> node is computed here instead. That is
/// safe in a way an in-memory <i>filter</i> or <i>aggregate</i> would not be: both operands are
/// values of the row Salesforce already grouped and returned, so no row is missing and no group is
/// half-counted — only the last arithmetic step happens locally.
/// </remarks>
public sealed record SoqlMathValue(MathOperator Operator, SoqlValueSource Left, SoqlValueSource Right) : SoqlValueSource;

/// <summary>
/// Calendar boundary <c>DATEDIFF(unit, start, end)</c> computed over two values in the answered row,
/// evaluated in the query's time zone and week start.
/// </summary>
public sealed record SoqlDateDiffValue(
    string Unit,
    SoqlValueSource Start,
    SoqlValueSource End,
    TimeZoneInfo Zone,
    DayOfWeek WeekStart) : SoqlValueSource;

/// <summary>One output column: the alias the caller asked for, and where its value comes from.</summary>
public sealed record SoqlProjectionItem(string Alias, SoqlValueSource Source);

/// <summary>
/// The shape of the answer: which columns to build and whether Salesforce ran this as an aggregate
/// query (which changes how records come back and rules out paging).
/// </summary>
public sealed record SoqlProjection(IReadOnlyList<SoqlProjectionItem> Items, bool IsAggregate);

/// <summary>A compiled query: the SOQL text and the mapping that reads its answer.</summary>
public sealed record BuiltSoqlQuery(string Soql, SoqlProjection Projection);
