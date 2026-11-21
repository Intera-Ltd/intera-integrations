using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Intera.Jira.Api.Infrastructure;

/// <summary>A bound on one JQL date field: <c>updated</c>, <c>created</c>, <c>resolved</c>, <c>due</c>, <c>worklogDate</c>.</summary>
public sealed record JqlDateBound(string Field, DateOnly? From, DateOnly? To);

/// <summary>Everything an issue read narrows by, before it is written as JQL.</summary>
/// <param name="Scopes">Deployment and instance JQL restrictions, each already checked by <see cref="Jql.ValidateScope"/>.</param>
/// <param name="Projects">Project ids or keys that exist on the site.</param>
/// <param name="IssueKeys">Issue keys (<c>DEV-481</c>).</param>
/// <param name="IssueIds">Issue ids (<c>10481</c>).</param>
/// <param name="Dates">Date bounds, inclusive, in calendar days.</param>
public sealed record JqlIssueQuery(
    IReadOnlyList<string> Scopes,
    IReadOnlyList<string> Projects,
    IReadOnlyList<string> IssueKeys,
    IReadOnlyList<string> IssueIds,
    IReadOnlyList<JqlDateBound> Dates)
{
    public static readonly JqlIssueQuery Empty = new([], [], [], [], []);
}

/// <summary>
/// Writes the JQL of an issue read. The filter is pushed down only as a <b>superset</b> of the rows it matches —
/// the query DSL still runs in memory over what comes back — so every clause here may only ever widen.
/// </summary>
/// <remarks>
/// <para>
/// <b>Dates are widened by a day on each side.</b> JQL reads a date literal in the time zone of the user the grant
/// belongs to, which this service does not know, while the filter's bounds are in the query's own zone. One day of
/// slack on each end covers any pair of zones; the in-memory pass then cuts at the exact instant.
/// </para>
/// <para>
/// <b>Enhanced search refuses an unbounded query</b> (one with no restriction, e.g. only <c>ORDER BY</c>). Callers
/// always add a window, a scope or an issue list; should all be absent the query falls back to
/// <c>created &gt;= "1970-01-01"</c>, which restricts nothing yet is a restriction.
/// </para>
/// </remarks>
public static partial class Jql
{
    public const string AlwaysTrue = "created >= \"1970-01-01\"";

    /// <summary>A JQL string literal: double-quoted, with <c>"</c> and <c>\</c> escaped.</summary>
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    public static string Date(DateOnly date) => Quote(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    public static bool IsIssueKey(string value) => IssueKeyPattern().IsMatch(value);

    public static bool IsNumericId(string value) => value.Length is > 0 and <= 19 && value.All(char.IsAsciiDigit);

    /// <summary>The project key of an issue key (<c>DEV-481</c> → <c>DEV</c>); null when it is not one.</summary>
    public static string? ProjectKeyOf(string? issueKey)
    {
        if (issueKey is null || !IsIssueKey(issueKey))
        {
            return null;
        }

        return issueKey[..issueKey.LastIndexOf('-')];
    }

    /// <summary>
    /// Refuses a scope that could escape the parentheses it is wrapped in or reorder the search: unbalanced
    /// parentheses or quotes, or an <c>ORDER BY</c>. The deployment's scope is a ceiling an instance may only
    /// narrow, so an instance scope reading <c>x) OR (y</c> must not get through.
    /// </summary>
    public static string? ValidateScope(string? scope, string origin)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return null;
        }

        var text = scope.Trim();
        var outside = new StringBuilder(text.Length);
        var depth = 0;
        char? quote = null;

        for (var i = 0; i < text.Length; i++)
        {
            var character = text[i];

            if (quote is { } open)
            {
                if (character == '\\')
                {
                    i++;
                }
                else if (character == open)
                {
                    quote = null;
                }

                outside.Append(' ');
                continue;
            }

            switch (character)
            {
                case '"' or '\'':
                    quote = character;
                    outside.Append(' ');
                    continue;
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    if (depth < 0)
                    {
                        throw new JiraRequestException($"The JQL scope from {origin} closes a parenthesis it never opened: {text}");
                    }

                    break;
            }

            outside.Append(character);
        }

        if (quote is not null || depth != 0)
        {
            throw new JiraRequestException($"The JQL scope from {origin} has an unbalanced quote or parenthesis: {text}");
        }

        if (OrderBy().IsMatch(outside.ToString()))
        {
            throw new JiraRequestException($"The JQL scope from {origin} may not contain ORDER BY: {text}");
        }

        return text;
    }

    /// <summary>The JQL for <paramref name="query"/>: its clauses joined with AND, each scope in parentheses.</summary>
    public static string Build(JqlIssueQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var clauses = new List<string>();

        clauses.AddRange(query.Scopes.Where(static scope => !string.IsNullOrWhiteSpace(scope)).Select(static scope => $"({scope})"));

        if (query.Projects.Count > 0)
        {
            clauses.Add($"project in ({string.Join(", ", query.Projects.Select(Value))})");
        }

        // Keys and ids name the same thing two ways: either one matching is enough.
        var issueClauses = new List<string>(2);
        if (query.IssueKeys.Count > 0)
        {
            issueClauses.Add($"key in ({string.Join(", ", query.IssueKeys.Select(Quote))})");
        }

        if (query.IssueIds.Count > 0)
        {
            issueClauses.Add($"id in ({string.Join(", ", query.IssueIds)})");
        }

        if (issueClauses.Count == 1)
        {
            clauses.Add(issueClauses[0]);
        }
        else if (issueClauses.Count == 2)
        {
            clauses.Add($"({issueClauses[0]} OR {issueClauses[1]})");
        }

        foreach (var bound in query.Dates)
        {
            if (bound.From is { } from)
            {
                clauses.Add($"{bound.Field} >= {Date(from.AddDays(-1))}");
            }

            if (bound.To is { } to)
            {
                // Inclusive "to" plus a day of slack: everything before the start of to + 2 days.
                clauses.Add($"{bound.Field} < {Date(to.AddDays(2))}");
            }
        }

        return clauses.Count == 0 ? AlwaysTrue : string.Join(" AND ", clauses);
    }

    /// <summary>A project id goes bare, a key quoted (keys are letters, but quoting never hurts).</summary>
    private static string Value(string projectIdOrKey) => IsNumericId(projectIdOrKey) ? projectIdOrKey : Quote(projectIdOrKey);

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]*-[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex IssueKeyPattern();

    [GeneratedRegex(@"\border\s+by\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex OrderBy();
}
