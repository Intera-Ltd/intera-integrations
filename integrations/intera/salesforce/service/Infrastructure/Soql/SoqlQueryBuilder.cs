using System.Globalization;
using System.Text;
using Intera.Common.Infrastructure.Query;
using Intera.Common.Models.Query;

namespace Intera.Salesforce.Api.Infrastructure.Soql;

/// <summary>
/// Compiles a <see cref="QueryRequest"/> into one SOQL statement.
/// </summary>
/// <remarks>
/// The SOQL counterpart of <c>QuerySqlBuilder</c>. Salesforce has SOQL, and a Salesforce
/// org routinely holds six figures of Opportunities: a snapshot capped at N rows would answer
/// "opportunities closing next quarter" out of the newest N records and look right while being
/// wrong. Everything that decides <i>which rows exist</i> — the filter, the ordering, the limit, the
/// grouping — is therefore compiled and sent upstream.
/// <para>
/// The limits mirror <c>QuerySqlBuilder</c>'s so one DSL does not mean two different things
/// depending on which backend answers, with one addition: <see cref="MaxSkip"/>, which is
/// Salesforce's own hard cap on <c>OFFSET</c> and not a choice made here.
/// </para>
/// <para>
/// An aggregate carrying its own <c>filter</c> has no SOQL form (there is no <c>CASE</c>) and is
/// refused rather than quietly worked around, as is an aggregate over an <c>expression</c> (SOQL
/// aggregate functions take a field only). So is any field used in a clause Salesforce does not
/// allow it in — see <see cref="SoqlFieldUse"/>, which tracks <c>WHERE</c>, <c>ORDER BY</c>,
/// <c>GROUP BY</c> and aggregation separately because Salesforce does. A <c>math</c> node is not
/// refused: SOQL cannot do arithmetic in a SELECT, so its operands are selected and the arithmetic
/// is applied to the row Salesforce already grouped and returned. That is the one thing computed
/// locally, and it cannot change which rows or groups exist.
/// </para>
/// </remarks>
public sealed class SoqlQueryBuilder(SoqlObject sobject)
{
    private const int MaxExpressions = 100;
    private const int MaxFilterDepth = 16;
    private const int MaxFilterItems = 200;
    private const int MaxInValues = 1000;
    private const int MaxTop = 10000;
    private const int MaxOrderItems = 20;

    /// <summary>
    /// Salesforce refuses an <c>OFFSET</c> above this, and there is no second spelling for it: past
    /// 2000 rows the API's own answer is a query locator, which is a cursor rather than a page and
    /// cannot serve "jump to page 40". Refusing here says so; letting it through would surface as an
    /// upstream <c>NUMBER_OUTSIDE_VALID_RANGE</c>.
    /// </summary>
    public const int MaxSkip = 2000;

    private readonly List<string> selectItems = [];
    private readonly Dictionary<string, string> aggregateAliases = new(StringComparer.Ordinal);
    private readonly HashSet<string> selectedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> inferredGroupBy = [];

    private DateTimeOffset queryNow;
    private TimeZoneInfo zone = TimeZoneInfo.Local;
    private DayOfWeek weekStart = DayOfWeek.Monday;
    private int aliasIndex;
    private int filterItemCount;
    private bool hasAggregate;

    /// <summary>
    /// Compiles the request. <paramref name="defaultLimit"/> is the <c>LIMIT</c> applied when the
    /// caller named no <c>top</c> — there is no unbounded query here, since a passthrough over an
    /// org's whole Account table is not something a dashboard should be able to ask for by omission.
    /// </summary>
    public BuiltSoqlQuery Build(QueryRequest? query, int defaultLimit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(defaultLimit, 1);

        // Resolved once per query so every relativeDate in it anchors on the same instant, the same
        // rule QuerySqlBuilder follows.
        queryNow = DateTimeOffset.UtcNow;
        zone = RelativeDateResolver.FindTimeZone(query?.TimeZone) ?? TimeZoneInfo.Local;
        weekStart = RelativeDateResolver.ParseWeekStart(query?.WeekStart);

        var expressions = query?.Result?.Expressions ?? [];
        if (expressions.Count > MaxExpressions)
        {
            throw new ArgumentException($"Too many result expressions. Max allowed: {MaxExpressions}.");
        }

        foreach (var expression in expressions)
        {
            QueryExpressionRules.Validate(expression.Expression);
        }

        hasAggregate = expressions.Any(expression => ContainsAggregate(expression.Expression));

        ValidateTop(query);
        ValidateSkip(query);

        var projection = hasAggregate || expressions.Count > 0
            ? BuildProjection(expressions)
            : BuildPassthroughProjection();

        var soql = new StringBuilder();
        soql.Append("SELECT ");
        soql.Append(string.Join(", ", selectItems));
        soql.Append(" FROM ");
        soql.Append(sobject.ObjectName);

        var where = BuildWhere(query);
        if (where.Length > 0)
        {
            soql.Append(" WHERE ");
            soql.Append(where);
        }

        AppendGroupBy(soql, query);
        AppendOrderBy(soql, query);
        AppendLimit(soql, query, defaultLimit);

        return new BuiltSoqlQuery(soql.ToString(), projection);
    }

    /// <summary>
    /// The row count of the same object and filter, for the <c>X-Total-Count</c> header of a paged
    /// query.
    /// </summary>
    /// <remarks>
    /// <c>SELECT COUNT()</c> is Salesforce's own counting form: it answers with a <c>totalSize</c>
    /// and no records at all, so a count of a million rows costs one small response. A fresh builder
    /// is required per call, as with <c>QuerySqlBuilder</c> — this one accumulates SELECT state.
    /// <para>
    /// Aggregates are refused rather than counted, for the reason <c>QuerySqlBuilder.BuildCount</c>
    /// gives: <c>COUNT()</c> over a grouped query answers "how many groups", which is not the number
    /// a pager is asking for.
    /// </para>
    /// </remarks>
    public string BuildCount(QueryRequest? query)
    {
        if (query?.Result?.Expressions.Any(expression => ContainsAggregate(expression.Expression)) == true)
        {
            throw new ArgumentException("Total count is not supported for aggregate result queries.");
        }

        queryNow = DateTimeOffset.UtcNow;
        zone = RelativeDateResolver.FindTimeZone(query?.TimeZone) ?? TimeZoneInfo.Local;
        weekStart = RelativeDateResolver.ParseWeekStart(query?.WeekStart);

        var soql = new StringBuilder();
        soql.Append("SELECT COUNT() FROM ");
        soql.Append(sobject.ObjectName);

        var where = BuildWhere(query);
        if (where.Length > 0)
        {
            soql.Append(" WHERE ");
            soql.Append(where);
        }

        return soql.ToString();
    }

    /// <summary>
    /// "No result expressions" means passthrough, which SOQL cannot spell: there is no
    /// <c>SELECT *</c>.
    /// </summary>
    /// <remarks>
    /// Expanding the allowlist is the honest translation here, and not the thing CLAUDE.md warns
    /// against. That warning is about a <i>plugin</i> synthesising a field list, which drifts from
    /// the API's own allowed set and silently breaks a passthrough fetch. This list <b>is</b> the
    /// API's allowed set — the same object the filter and the ordering resolve against — so there is
    /// nothing for it to drift from.
    /// </remarks>
    private SoqlProjection BuildPassthroughProjection()
    {
        var items = new List<SoqlProjectionItem>(sobject.Fields.Count);

        foreach (var field in sobject.Fields)
        {
            AddPathToSelect(field.Path);
            items.Add(new SoqlProjectionItem(field.Name, new SoqlPathValue(field.Path, field.Kind)));
        }

        return new SoqlProjection(items, IsAggregate: false);
    }

    private SoqlProjection BuildProjection(IReadOnlyList<ResultExpression> expressions)
    {
        var items = new List<SoqlProjectionItem>(expressions.Count);

        foreach (var expression in expressions)
        {
            if (string.IsNullOrWhiteSpace(expression.Alias))
            {
                throw new ArgumentException("Result alias is required.");
            }

            items.Add(new SoqlProjectionItem(expression.Alias, BuildValueSource(expression.Expression)));
        }

        if (selectItems.Count == 0)
        {
            // Every expression was a literal. SOQL still needs something to select, and Id is the one
            // field every sObject has.
            AddPathToSelect("Id");
        }

        return new SoqlProjection(items, hasAggregate);
    }

    private SoqlValueSource BuildValueSource(ExpressionNode node)
    {
        return node switch
        {
            ValueExpression value => new SoqlLiteralValue(value.Value is RelativeDateValue relativeDate
                ? RelativeDateResolver.Resolve(relativeDate, queryNow, zone, weekStart)
                : value.Value),
            FieldExpression field => BuildFieldSource(field.Field),
            NowExpression now => new SoqlLiteralValue(RelativeDateResolver.ResolveNow(now.Anchor, queryNow, zone)),
            DateDiffExpression dateDiff => new SoqlDateDiffValue(
                RelativeDateResolver.NormalizeUnit(dateDiff.Unit, "dateDiff unit"),
                BuildValueSource(dateDiff.Start),
                BuildValueSource(dateDiff.End),
                zone,
                weekStart),
            AggregateExpression aggregate => BuildAggregateSource(aggregate),
            MathExpression math => new SoqlMathValue(
                math.Operator,
                BuildValueSource(math.Left),
                BuildValueSource(math.Right)),
            _ => throw new ArgumentException($"Unsupported expression type '{node.GetType().Name}'.")
        };
    }

    private SoqlValueSource BuildFieldSource(string fieldName)
    {
        if (!hasAggregate)
        {
            var plain = sobject.Resolve(fieldName);
            AddPathToSelect(plain.Path);
            return new SoqlPathValue(plain.Path, plain.Kind);
        }

        // In a grouped query a bare field is a grouping key — so it is checked against Group, not
        // Filter — and it is selected under a generated alias, because Salesforce returns a parent
        // traversal under the leaf name only (Account.Name comes back as "Name"), which two
        // different traversals would collide on.
        var field = sobject.ResolveFor(fieldName, SoqlFieldUse.Group);
        AddInferredGroupBy(field.Path);
        return new SoqlAliasValue(AddAliasedToSelect(field.Path));
    }

    private SoqlValueSource BuildAggregateSource(AggregateExpression node)
    {
        if (node.Filter is not null)
        {
            // The SQL builder compiles this to SUM(CASE WHEN … END). SOQL has no CASE and no
            // conditional aggregate, and faking it would mean pulling the rows and adding them up
            // here — which is exactly the capped-snapshot answer this project exists to avoid.
            throw new ArgumentException(
                $"Aggregate '{node.Action}' with its own filter is not supported against Salesforce: SOQL has no conditional aggregate. Move the condition into the query filter.");
        }

        if (node.Expression is not null)
        {
            // SUM(Amount * Probability) has no SOQL form: its aggregate functions take a field and
            // nothing else. Computing it here would again mean pulling every row, so it is refused
            // for the same reason as the conditional aggregate above.
            throw new ArgumentException(
                $"Aggregate '{node.Action}' over an 'expression' is not supported against Salesforce: SOQL aggregate functions take a single field. Aggregate a field instead.");
        }

        return node.Action switch
        {
            AggregateAction.Sum => new SoqlAliasValue(AddAliasedToSelect($"SUM({NumericField(node)})")),
            AggregateAction.Avg => new SoqlAliasValue(AddAliasedToSelect($"AVG({NumericField(node)})")),
            AggregateAction.Min => new SoqlAliasValue(AddAliasedToSelect($"MIN({ComparableField(node)})")),
            AggregateAction.Max => new SoqlAliasValue(AddAliasedToSelect($"MAX({ComparableField(node)})")),
            AggregateAction.Count => new SoqlAliasValue(AddAliasedToSelect($"COUNT({CountedField(node)})")),
            AggregateAction.CountDistinct => new SoqlAliasValue(AddAliasedToSelect($"COUNT_DISTINCT({ComparableField(node)})")),
            AggregateAction.Exists => new SoqlExistsValue(AddAliasedToSelect($"COUNT({CountedField(node)})")),
            _ => throw new ArgumentException($"Unsupported aggregate action '{node.Action}'.")
        };
    }

    /// <summary>
    /// The field of a <c>count</c>/<c>exists</c> node, or <c>Id</c> when it names none.
    /// </summary>
    /// <remarks>
    /// Not the bare <c>COUNT()</c> Salesforce also accepts: that form returns only a
    /// <c>totalSize</c> and no record, so it cannot sit beside other expressions or a
    /// <c>GROUP BY</c>. <c>COUNT(Id)</c> answers the same number in a normal aggregate record, and
    /// Id is never null, so nothing is dropped.
    /// </remarks>
    private string CountedField(AggregateExpression node)
    {
        return string.IsNullOrWhiteSpace(node.Field)
            ? "Id"
            : sobject.ResolveFor(node.Field, SoqlFieldUse.Aggregate).Path;
    }

    private string NumericField(AggregateExpression node)
    {
        var field = sobject.ResolveFor(RequireField(node), SoqlFieldUse.Aggregate);

        return field.Kind == SoqlFieldKind.Number
            ? field.Path
            : throw new ArgumentException($"Aggregate '{node.Action}' needs a numeric field, and '{field.Name}' is not one.");
    }

    private string ComparableField(AggregateExpression node) =>
        sobject.ResolveFor(RequireField(node), SoqlFieldUse.Aggregate).Path;

    private static string RequireField(AggregateExpression node) =>
        string.IsNullOrWhiteSpace(node.Field)
            ? throw new ArgumentException($"Aggregate action '{node.Action}' requires 'field'.")
            : node.Field;

    private void AddPathToSelect(string path)
    {
        if (selectedPaths.Add(path))
        {
            selectItems.Add(path);
        }
    }

    /// <summary>
    /// Adds a SELECT item under a generated alias, reusing the alias when the same expression is
    /// asked for twice.
    /// </summary>
    /// <remarks>
    /// Generated (<c>a0</c>, <c>a1</c>, …) rather than the caller's own alias: SOQL identifiers are
    /// a narrow set, while a result alias comes from a dashboard and is arbitrary text. Mapping back
    /// through the projection keeps the caller's alias intact without ever putting it in the query.
    /// </remarks>
    private string AddAliasedToSelect(string expression)
    {
        if (aggregateAliases.TryGetValue(expression, out var existing))
        {
            return existing;
        }

        var alias = $"a{aliasIndex.ToString(CultureInfo.InvariantCulture)}";
        aliasIndex++;

        aggregateAliases[expression] = alias;
        selectItems.Add($"{expression} {alias}");

        return alias;
    }

    private void AddInferredGroupBy(string path)
    {
        if (!inferredGroupBy.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            inferredGroupBy.Add(path);
        }
    }

    private void AppendGroupBy(StringBuilder soql, QueryRequest? query)
    {
        if (!hasAggregate)
        {
            return;
        }

        // An explicit groupBy IS the grouping; the keys inferred from bare field references are the
        // fallback for callers written before the contract named them (canonical contract,
        // 2026-08-28).
        var explicitKeys = (query?.Result?.GroupBy ?? [])
            .Select(field => sobject.ResolveFor(field, SoqlFieldUse.Group).Path)
            .ToArray();

        var keys = explicitKeys.Length > 0 ? explicitKeys : [.. inferredGroupBy];
        if (keys.Length == 0)
        {
            return;
        }

        soql.Append(" GROUP BY ");
        soql.Append(string.Join(", ", keys.Distinct(StringComparer.OrdinalIgnoreCase)));
    }

    private void AppendOrderBy(StringBuilder soql, QueryRequest? query)
    {
        var order = query?.Order ?? [];
        if (order.Count == 0)
        {
            return;
        }

        if (order.Count > MaxOrderItems)
        {
            throw new ArgumentException($"Too many order items. Max allowed: {MaxOrderItems}.");
        }

        if (hasAggregate)
        {
            throw new ArgumentException("Order is not supported for aggregate result queries yet.");
        }

        var parts = order.Select(item =>
        {
            if (string.IsNullOrWhiteSpace(item.Field))
            {
                throw new ArgumentException("Order field is required.");
            }

            var field = sobject.ResolveFor(item.Field, SoqlFieldUse.Sort);
            var direction = item.Direction.Trim().ToUpperInvariant() switch
            {
                "ASC" => "ASC",
                "DESC" => "DESC",
                _ => throw new ArgumentException($"Unsupported order direction '{item.Direction}'.")
            };

            return $"{field.Path} {direction}";
        });

        soql.Append(" ORDER BY ");
        soql.Append(string.Join(", ", parts));
    }

    /// <summary>
    /// Appends <c>LIMIT</c> and, for a page, <c>OFFSET</c> — in that order, which is SOQL's, the
    /// reverse of the SQL <c>OFFSET … FETCH</c> spelling.
    /// </summary>
    private void AppendLimit(StringBuilder soql, QueryRequest? query, int defaultLimit)
    {
        if (hasAggregate)
        {
            // Top and Skip were already refused above; a grouped query returns one row per group and
            // needs no cap of its own beyond the org's own aggregate limits.
            return;
        }

        var limit = query?.Top ?? defaultLimit;
        soql.Append(CultureInfo.InvariantCulture, $" LIMIT {limit}");

        if (query?.Skip is { } skip)
        {
            soql.Append(CultureInfo.InvariantCulture, $" OFFSET {skip}");
        }
    }

    private static void ValidateTop(QueryRequest? query)
    {
        if (query?.Top is null)
        {
            return;
        }

        if (query.Top <= 0 || query.Top > MaxTop)
        {
            throw new ArgumentException($"Top must be between 1 and {MaxTop}.");
        }

        if (query.Result?.Expressions.Any(expression => ContainsAggregate(expression.Expression)) == true)
        {
            throw new ArgumentException("Top is not supported for aggregate result queries: without an order, a limited set of groups is not reproducible.");
        }
    }

    private static void ValidateSkip(QueryRequest? query)
    {
        if (query?.Skip is not { } skip)
        {
            return;
        }

        if (skip < 0)
        {
            throw new ArgumentException("Skip must be zero or greater.");
        }

        if (skip > MaxSkip)
        {
            throw new ArgumentException($"Skip must be at most {MaxSkip}: Salesforce refuses a larger OFFSET. Narrow the filter instead of paging further.");
        }

        if (query.Result?.Expressions.Any(expression => ContainsAggregate(expression.Expression)) == true)
        {
            throw new ArgumentException("Skip is not supported for aggregate result queries.");
        }
    }

    private static bool ContainsAggregate(ExpressionNode node)
    {
        return node switch
        {
            AggregateExpression => true,
            MathExpression math => ContainsAggregate(math.Left) || ContainsAggregate(math.Right),
            DateDiffExpression dateDiff => ContainsAggregate(dateDiff.Start) || ContainsAggregate(dateDiff.End),
            FieldExpression or ValueExpression or NowExpression => false,
            _ => throw new ArgumentException($"Unsupported expression type '{node.GetType().Name}'.")
        };
    }

    private string BuildWhere(QueryRequest? query)
    {
        if (query is null)
        {
            return string.Empty;
        }

        var parts = new List<string>();

        if (query.Filter is not null)
        {
            parts.Add(BuildFilter(query.Filter, 0));
        }

        foreach (var filter in query.Filters)
        {
            parts.Add(BuildFilter(filter, 0));
        }

        var nonEmpty = parts.Where(static part => !string.IsNullOrWhiteSpace(part)).ToArray();

        return nonEmpty.Length == 0
            ? string.Empty
            : string.Join(" AND ", nonEmpty.Select(static part => $"({part})"));
    }

    private string BuildFilter(FilterNode node, int depth)
    {
        if (depth > MaxFilterDepth)
        {
            throw new ArgumentException($"Filter is too deep. Max allowed depth: {MaxFilterDepth}.");
        }

        if (node is FilterExpressionCondition exprCondition)
        {
            ValidateExpressionCondition(exprCondition);

            if (FilterExpressionNormalizer.TryNormalize(exprCondition) is { } normalized)
            {
                return BuildFilter(normalized, depth);
            }

            throw new ArgumentException(
                "Formula filter conditions over multiple fields or arithmetic are not supported against Salesforce: SOQL WHERE clauses only support single-field comparisons.");
        }

        filterItemCount++;
        if (filterItemCount > MaxFilterItems)
        {
            throw new ArgumentException($"Too many filter items. Max allowed: {MaxFilterItems}.");
        }

        return node switch
        {
            FilterCondition condition => BuildCondition(condition),
            FilterGroup group => BuildGroup(group, depth),
            _ => throw new ArgumentException($"Unsupported filter type '{node.GetType().Name}'.")
        };
    }

    private static void ValidateExpressionCondition(FilterExpressionCondition condition)
    {
        QueryExpressionRules.ValidateRowScope(condition.Left);

        if (condition.Operator is FilterOperator.IsNull or FilterOperator.IsNotNull)
        {
            if (condition.Right is not null)
            {
                throw new ArgumentException($"Filter expression condition with '{condition.Operator}' must not specify 'right'.");
            }

            return;
        }

        if (condition.Operator is FilterOperator.In or FilterOperator.NotIn or FilterOperator.Like or FilterOperator.NotLike)
        {
            throw new ArgumentException($"Filter expression condition does not support operator '{condition.Operator}'.");
        }

        if (condition.Right is null)
        {
            throw new ArgumentException($"Filter expression condition with '{condition.Operator}' requires 'right'.");
        }

        QueryExpressionRules.ValidateRowScope(condition.Right);
    }

    private string BuildGroup(FilterGroup group, int depth)
    {
        var parts = group.Items
            .Select(item => BuildFilter(item, depth + 1))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();

        return group.Operator switch
        {
            LogicalOperator.And => parts.Length == 0 ? string.Empty : $"({string.Join(" AND ", parts)})",
            LogicalOperator.Or => parts.Length == 0 ? string.Empty : $"({string.Join(" OR ", parts)})",
            LogicalOperator.Not => parts.Length == 1
                ? $"(NOT {parts[0]})"
                : throw new ArgumentException("NOT filter group requires exactly one item."),
            _ => throw new ArgumentException($"Unsupported logical operator '{group.Operator}'.")
        };
    }

    private string BuildCondition(FilterCondition condition)
    {
        var field = sobject.ResolveFor(condition.Field, SoqlFieldUse.Filter);

        return condition.Operator switch
        {
            FilterOperator.Equal => Compare(field, "=", condition),
            FilterOperator.NotEqual => Compare(field, "!=", condition),
            FilterOperator.LessThan => Compare(field, "<", condition),
            FilterOperator.LessThanOrEqual => Compare(field, "<=", condition),
            FilterOperator.GreaterThan => Compare(field, ">", condition),
            FilterOperator.GreaterThanOrEqual => Compare(field, ">=", condition),
            FilterOperator.Like => Like(field, condition, negate: false),
            FilterOperator.NotLike => Like(field, condition, negate: true),
            FilterOperator.In => In(field, condition, negate: false),
            FilterOperator.NotIn => In(field, condition, negate: true),

            // SOQL has no IS NULL: the null comparison is spelled with the ordinary operators.
            FilterOperator.IsNull => $"({field.Path} = null)",
            FilterOperator.IsNotNull => $"({field.Path} != null)",
            _ => throw new ArgumentException($"Unsupported filter operator '{condition.Operator}'.")
        };
    }

    private string Compare(SoqlField field, string soqlOperator, FilterCondition condition)
    {
        if (condition.Value is null)
        {
            throw new ArgumentException($"Operator '{condition.Operator}' requires 'value'.");
        }

        return $"({field.Path} {soqlOperator} {RenderValue(field, condition.Value)})";
    }

    private string Like(SoqlField field, FilterCondition condition, bool negate)
    {
        if (condition.Value is null)
        {
            throw new ArgumentException($"Operator '{condition.Operator}' requires 'value'.");
        }

        if (condition.Value is FieldRefValue)
        {
            throw new ArgumentException("Field-to-field comparisons are not supported against Salesforce: SOQL WHERE clauses require literal right-hand values.");
        }

        if (field.Kind != SoqlFieldKind.Text)
        {
            // Salesforce rejects LIKE on anything but a string field. Saying so here names the field;
            // letting it through returns MALFORMED_QUERY with the whole query echoed back instead.
            throw new ArgumentException($"Operator '{condition.Operator}' needs a text field, and '{field.Name}' is not one.");
        }

        var pattern = SoqlLiteral.RenderLikePattern(condition.Value);
        return negate ? $"(NOT {field.Path} LIKE {pattern})" : $"({field.Path} LIKE {pattern})";
    }

    private string In(SoqlField field, FilterCondition condition, bool negate)
    {
        if (condition.Value is not object[] values)
        {
            throw new ArgumentException("IN and NOT_IN operators require array 'value'.");
        }

        if (values.Length > MaxInValues)
        {
            throw new ArgumentException($"Too many IN values. Max allowed: {MaxInValues}.");
        }

        if (values.Any(static value => value is RelativeDateValue))
        {
            throw new ArgumentException("IN and NOT_IN operators do not support relativeDate values.");
        }

        if (values.Any(static value => value is FieldRefValue))
        {
            throw new ArgumentException("IN and NOT_IN operators do not support field reference values.");
        }

        if (values.Length == 0)
        {
            // SOQL rejects an empty IN list, and it has no constant true/false expression to stand in
            // for one. Id is on every sObject and is never null, so these two are the always-false and
            // always-true conditions the empty list means.
            return negate ? "(Id != null)" : "(Id = null)";
        }

        var rendered = values.Select(value => RenderValue(field, value));
        var soqlOperator = negate ? "NOT IN" : "IN";

        return $"({field.Path} {soqlOperator} ({string.Join(", ", rendered)}))";
    }

    /// <summary>
    /// Renders one filter value, resolving a <see cref="RelativeDateValue"/> to the instant it means
    /// first.
    /// </summary>
    /// <remarks>
    /// SOQL has its own relative date literals (<c>LAST_N_DAYS:90</c>, <c>THIS_MONTH</c>), and they
    /// are deliberately not used. They resolve against the Salesforce <i>user's</i> time zone, while
    /// every other engine in this solution resolves against <see cref="QueryRequest.TimeZone"/> — so
    /// the same saved filter would mean a different window depending on which backend answered it,
    /// silently. That is the exact bug <c>QueryRequest.TimeZone</c> was added to close, and reopening
    /// it to save a few characters of query text is not a trade worth making.
    /// </remarks>
    private string RenderValue(SoqlField field, object? value)
    {
        if (value is FieldRefValue)
        {
            throw new ArgumentException("Field-to-field comparisons are not supported against Salesforce: SOQL WHERE clauses require literal right-hand values.");
        }

        var resolved = value is RelativeDateValue relativeDate
            ? RelativeDateResolver.Resolve(relativeDate, queryNow, zone, weekStart)
            : value;

        return SoqlLiteral.Render(field, resolved, zone);
    }
}
