using Intera.Common.Models.Query;
using System.Globalization;

namespace Intera.Common.Infrastructure.Query;

/// <summary>
/// The parts of a <see cref="QueryRequest"/> an upstream API can answer itself: a reporting window
/// and the entity keys the filter names.
/// </summary>
/// <param name="From">Lower bound implied by the filter's conditions on the date field, if any.</param>
/// <param name="To">Upper bound implied by the filter's conditions on the date field, if any.</param>
/// <param name="Keys">
/// Per key field, the values the filter names through <c>=</c> / <c>IN</c> — the only operators a URL
/// parameter can carry.
/// </param>
/// <param name="LocalConditions">
/// Conditions on fields a locally cached catalog already knows, kept whole (any operator) so they can
/// be evaluated against that catalog instead of upstream.
/// </param>
public sealed record QueryPushdown(
    DateOnly? From,
    DateOnly? To,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Keys,
    IReadOnlyList<FilterCondition> LocalConditions)
{
    public static readonly QueryPushdown Empty = new(
        null,
        null,
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
        []);

    public bool HasDateRange => From is not null || To is not null;

    /// <summary>Whether the filter says anything about which entities are wanted.</summary>
    public bool NamesEntities => Keys.Count > 0 || LocalConditions.Count > 0;

    public IReadOnlyList<string> Values(string field)
    {
        return Keys.TryGetValue(field, out var values) ? values : [];
    }

    public string? SingleValue(string field)
    {
        var values = Values(field);
        return values.Count == 1 ? values[0] : null;
    }
}

/// <summary>
/// Reads the filter tree for conditions the upstream API can enforce, so a request for one entity over
/// one week costs one upstream call instead of a fetch-everything-then-filter sweep.
/// </summary>
/// <remarks>
/// <para>
/// Shared across HTTP integration services that can push equality, set, or date-window predicates
/// down into upstream API queries before applying <see cref="InMemoryQueryEngine"/> over the fetched rows.
/// </para>
/// <para>
/// Extraction is deliberately one-way: nothing is removed from the <see cref="QueryRequest"/>, which is
/// still evaluated in full by <see cref="InMemoryQueryEngine"/> afterwards. That makes the pushdown a
/// pure optimisation — it only ever has to produce a <b>superset</b> of the matching rows, never the
/// exact set, and a case it fails to recognise costs performance rather than correctness.
/// </para>
/// <para>
/// Only the implicit top-level AND chain is walked (<c>filter</c>, each entry of <c>filters</c>, and
/// nested <c>group</c> nodes whose operator is AND), because only there is every condition a necessary
/// one. Descending into an OR or a NOT would narrow the fetch to rows the filter does not actually
/// require, and rows would silently go missing.
/// </para>
/// </remarks>
public static class QueryPushdownExtractor
{
    /// <param name="query">The request to read. <c>null</c> yields <see cref="QueryPushdown.Empty"/>.</param>
    /// <param name="dateField">Field mapped to the upstream reporting-window parameters.</param>
    /// <param name="keyFields">
    /// Fields whose values can identify an entity in an upstream request. Only <c>=</c> and <c>IN</c>
    /// yield usable values here, since a URL parameter can carry nothing else.
    /// </param>
    /// <param name="localFields">
    /// Fields the (cached) catalog already knows. Conditions on these are kept whole — any operator,
    /// <c>LIKE</c> included — and evaluated against the catalog to decide which entities are worth a
    /// detail request at all.
    /// </param>
    public static QueryPushdown Extract(
        QueryRequest? query,
        string? dateField,
        IReadOnlyCollection<string> keyFields,
        IReadOnlyCollection<string>? localFields = null,
        DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(keyFields);

        if (query is null)
        {
            return QueryPushdown.Empty;
        }

        var timeZone = RelativeDateResolver.FindTimeZone(query.TimeZone);
        var weekStart = RelativeDateResolver.ParseWeekStart(query.WeekStart);
        var effectiveNowUtc = nowUtc ?? DateTimeOffset.UtcNow;
        var state = new ExtractionState(dateField, keyFields, localFields ?? [], effectiveNowUtc, timeZone, weekStart);

        if (query.Filter is not null)
        {
            Walk(query.Filter, state, depth: 0);
        }

        foreach (var filter in query.Filters)
        {
            Walk(filter, state, depth: 0);
        }

        return state.Build();
    }

    private static void Walk(FilterNode node, ExtractionState state, int depth)
    {
        // Matches InMemoryQueryEngine's own depth cap; a deeper tree is rejected there, and stopping
        // here just means the tail is not pushed down.
        if (depth > 16)
        {
            return;
        }

        if (node is FilterExpressionCondition expressionCondition)
        {
            var normalized = FilterExpressionNormalizer.TryNormalize(expressionCondition);
            if (normalized is null)
            {
                return;
            }

            node = normalized;
        }

        switch (node)
        {
            case FilterGroup { Operator: LogicalOperator.And } group:
                foreach (var item in group.Items)
                {
                    Walk(item, state, depth + 1);
                }

                break;

            case FilterCondition condition:
                state.Accept(condition);
                break;

            default:
                // OR / NOT groups: not a necessary condition, so nothing here can be pushed down.
                break;
        }
    }

    private sealed class ExtractionState(
        string? dateField,
        IReadOnlyCollection<string> keyFields,
        IReadOnlyCollection<string> localFields,
        DateTimeOffset nowUtc,
        TimeZoneInfo? timeZone,
        DayOfWeek weekStart)
    {
        private readonly Dictionary<string, List<string>> keys = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<FilterCondition> localConditions = [];

        private DateOnly? from;
        private DateOnly? to;

        public void Accept(FilterCondition condition)
        {
            if (dateField is not null && string.Equals(condition.Field, dateField, StringComparison.OrdinalIgnoreCase))
            {
                AcceptDate(condition);
                return;
            }

            if (localFields.Any(name => string.Equals(name, condition.Field, StringComparison.OrdinalIgnoreCase)))
            {
                localConditions.Add(condition);
            }

            var field = keyFields.FirstOrDefault(name => string.Equals(name, condition.Field, StringComparison.OrdinalIgnoreCase));
            if (field is not null)
            {
                AcceptKey(field, condition);
            }
        }

        public QueryPushdown Build()
        {
            var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (field, values) in keys)
            {
                result[field] = values;
            }

            return new QueryPushdown(from, to, result, localConditions);
        }

        private void AcceptDate(FilterCondition condition)
        {
            switch (condition.Operator)
            {
                case FilterOperator.GreaterThan or FilterOperator.GreaterThanOrEqual:
                    // The upstream range is inclusive on both ends; a strict '>' therefore fetches one
                    // extra day, which the in-memory pass drops.
                    if (TryReadDate(condition.Value, out var lower))
                    {
                        from = from is null || lower > from ? lower : from;
                    }

                    break;

                case FilterOperator.LessThan or FilterOperator.LessThanOrEqual:
                    if (TryReadDate(condition.Value, out var upper))
                    {
                        to = to is null || upper < to ? upper : to;
                    }

                    break;

                case FilterOperator.Equal:
                    if (TryReadDate(condition.Value, out var exact))
                    {
                        from = from is null || exact > from ? exact : from;
                        to = to is null || exact < to ? exact : to;
                    }

                    break;

                case FilterOperator.In:
                    // A superset: the API has no IN, so request the whole span the list covers.
                    if (condition.Value is object[] values)
                    {
                        var dates = values.Select(value => TryReadDate(value, out var date) ? date : (DateOnly?)null).ToArray();
                        if (dates.Length > 0 && Array.TrueForAll(dates, static date => date is not null))
                        {
                            var min = dates.Min()!.Value;
                            var max = dates.Max()!.Value;
                            from = from is null || min > from ? min : from;
                            to = to is null || max < to ? max : to;
                        }
                    }

                    break;

                default:
                    break;
            }
        }

        private void AcceptKey(string field, FilterCondition condition)
        {
            switch (condition.Operator)
            {
                case FilterOperator.Equal:
                    AddValue(field, condition.Value);
                    break;

                case FilterOperator.In:
                    if (condition.Value is object[] values)
                    {
                        foreach (var value in values)
                        {
                            AddValue(field, value);
                        }
                    }

                    break;

                default:
                    // '!=', LIKE, IS_NULL and friends name no entity to fetch.
                    break;
            }
        }

        private void AddValue(string field, object? value)
        {
            var text = ReadText(value);
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (!keys.TryGetValue(field, out var list))
            {
                list = [];
                keys[field] = list;
            }

            if (!list.Contains(text, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(text);
            }
        }

        private static string? ReadText(object? value)
        {
            return value switch
            {
                null => null,
                string text => text.Trim(),
                RelativeDateValue or FieldRefValue => null,
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString()
            };
        }

        private bool TryReadDate(object? value, out DateOnly date)
        {
            switch (value)
            {
                case RelativeDateValue relativeDate:
                    date = DateOnly.FromDateTime(RelativeDateResolver.Resolve(relativeDate, nowUtc, timeZone, weekStart));
                    return true;

                case DateTime dateTime:
                    date = DateOnly.FromDateTime(dateTime);
                    return true;

                case string text when DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed):
                    date = DateOnly.FromDateTime(parsed);
                    return true;

                default:
                    date = default;
                    return false;
            }
        }
    }
}
