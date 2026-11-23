using Intera.Common.Models.Query;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Intera.Common.Infrastructure.Query;

/// <summary>
/// Evaluates a <see cref="QueryRequest"/> (filter/result DSL) directly over in-memory rows
/// for API integrations whose data does not come from a SQL source.
/// </summary>
public sealed class InMemoryQueryEngine(IReadOnlyDictionary<string, InMemoryQueryField> fields)
{
    private const int MaxExpressions = 100;
    private const int MaxFilterDepth = 16;
    private const int MaxFilterItems = 200;
    private const int MaxInValues = 1000;
    private const int MaxTop = 10000;
    private const int MaxOrderItems = 20;

    private int filterItemCount;

    /// <summary>
    /// Each aggregate's filter, compiled once per <see cref="Execute"/> call and reused for every group.
    /// </summary>
    /// <remarks>
    /// Compiling it per group counted the same filter nodes again for every group against
    /// <c>MaxFilterItems</c>: eleven filtered aggregates grouped over sixty hosts read as ~660 filter
    /// items and the query was refused, although the filter itself holds a dozen nodes.
    /// </remarks>
    private readonly Dictionary<AggregateExpression, Func<IReadOnlyDictionary<string, object?>, bool>> aggregateFilters =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Each aggregate's operand — its field's accessor or its compiled row expression — built once
    /// per <see cref="Execute"/> call, before any row is grouped.
    /// </summary>
    /// <remarks>
    /// Built up front rather than on first use so an unknown field is refused even when the filter
    /// leaves no group to evaluate; and compiled rather than walked per row, since this runs once for
    /// every row of every group.
    /// </remarks>
    private readonly Dictionary<AggregateExpression, AggregateOperand> aggregateOperands =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Row count after filtering and before paging, from the last <see cref="Execute"/> call.
    /// </summary>
    /// <remarks>
    /// The in-memory sources fetch the whole snapshot anyway, so this costs nothing — unlike the
    /// SQL path, where the same number needs its own <c>COUNT</c> round-trip.
    /// </remarks>
    public int FilteredRowCount { get; private set; }

    private static IEnumerable<IReadOnlyDictionary<string, object?>> ApplyPaging(
        IEnumerable<IReadOnlyDictionary<string, object?>> rows,
        QueryRequest? query)
    {
        var paged = query?.Skip is not null ? rows.Skip(query.Skip.Value) : rows;
        return query?.Top is not null ? paged.Take(query.Top.Value) : paged;
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Execute(
        IEnumerable<IReadOnlyDictionary<string, object?>> source,
        QueryRequest? query)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (query?.Top is not null && (query.Top <= 0 || query.Top > MaxTop))
        {
            throw new ArgumentException($"Top must be between 1 and {MaxTop}.");
        }

        if (query?.Skip is not null && query.Skip < 0)
        {
            throw new ArgumentException("Skip must be zero or greater.");
        }

        // Resolved once per call: every relativeDate in this query anchors on the same instant,
        // so `>= start of month` and `< start of next month` cannot straddle a tick.
        aggregateFilters.Clear();
        aggregateOperands.Clear();
        queryNow = DateTimeOffset.UtcNow;
        queryTimeZone = RelativeDateResolver.FindTimeZone(query?.TimeZone);
        queryWeekStart = RelativeDateResolver.ParseWeekStart(query?.WeekStart);

        var predicate = BuildQueryPredicate(query);
        var filtered = source.Where(predicate).ToList();

        // Row count AFTER filtering and BEFORE paging — what a pager needs. Recorded on every
        // call so the caller can read it without re-running the filter.
        FilteredRowCount = filtered.Count;

        var expressions = query?.Result?.Expressions ?? [];

        if (expressions.Count == 0)
        {
            var passthrough = ApplyOrder(filtered, query);
            return ApplyPaging(passthrough, query).ToList();
        }

        if (expressions.Count > MaxExpressions)
        {
            throw new ArgumentException($"Too many result expressions. Max allowed: {MaxExpressions}.");
        }

        foreach (var expression in expressions)
        {
            if (string.IsNullOrWhiteSpace(expression.Alias))
            {
                throw new ArgumentException("Result alias is required.");
            }

            QueryExpressionRules.Validate(expression.Expression);
        }

        var hasAggregate = expressions.Any(expression => ContainsAggregate(expression.Expression));

        if (hasAggregate)
        {
            if ((query?.Order.Count ?? 0) > 0)
            {
                throw new ArgumentException("Order is not supported for aggregate result queries yet.");
            }

            if (query?.Skip is not null)
            {
                throw new ArgumentException("Skip is not supported for aggregate result queries.");
            }

            // See `QuerySqlBuilder`: without an order, "the first N groups" is N arbitrary groups.
            if (query?.Top is not null)
            {
                throw new ArgumentException("Top is not supported for aggregate result queries: without an order, a limited set of groups is not reproducible.");
            }

            foreach (var expression in expressions)
            {
                PrepareAggregates(expression.Expression);
            }

            // An explicit `groupBy` IS the grouping; the keys inferred from bare field references
            // are the fallback for callers written before the contract named them.
            var groupKeyFields = new List<string>();
            var explicitGroupBy = query?.Result?.GroupBy ?? [];
            if (explicitGroupBy.Count > 0)
            {
                foreach (var field in explicitGroupBy)
                {
                    AddGroupKey(groupKeyFields, ResolveField(field).Name);
                }
            }
            else
            {
                foreach (var expression in expressions)
                {
                    CollectGroupKeys(expression.Expression, groupKeyFields);
                }
            }

            IEnumerable<QueryRowGroup> groups = GroupRows(filtered, groupKeyFields);

            return groups
                .Select(group => (IReadOnlyDictionary<string, object?>)BuildRowResult(expressions, expression => EvaluateGroupExpression(expression, group)))
                .ToList();
        }

        var ordered = ApplyOrder(filtered, query);
        var limitedRows = ApplyPaging(ordered, query);

        return limitedRows
            .Select(row => (IReadOnlyDictionary<string, object?>)BuildRowResult(expressions, expression => EvaluateScalar(expression, row)))
            .ToList();
    }

    private static Dictionary<string, object?> BuildRowResult(
        IReadOnlyList<ResultExpression> expressions,
        Func<ExpressionNode, object?> evaluator)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var expression in expressions)
        {
            result[expression.Alias] = evaluator(expression.Expression);
        }

        return result;
    }

    // ----- Filtering -----

    private Func<IReadOnlyDictionary<string, object?>, bool> BuildQueryPredicate(QueryRequest? query)
    {
        if (query is null)
        {
            return static _ => true;
        }

        var parts = new List<Func<IReadOnlyDictionary<string, object?>, bool>>();

        if (query.Filter is not null)
        {
            parts.Add(BuildFilter(query.Filter, 0));
        }

        foreach (var filter in query.Filters)
        {
            parts.Add(BuildFilter(filter, 0));
        }

        if (parts.Count == 0)
        {
            return static _ => true;
        }

        return row => parts.All(part => part(row));
    }

    private Func<IReadOnlyDictionary<string, object?>, bool> BuildFilter(FilterNode node, int depth)
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
        }

        filterItemCount++;
        if (filterItemCount > MaxFilterItems)
        {
            throw new ArgumentException($"Too many filter items. Max allowed: {MaxFilterItems}.");
        }

        return node switch
        {
            FilterCondition condition => BuildFilterCondition(condition),
            FilterExpressionCondition expr => BuildExpressionCondition(expr),
            FilterGroup group => BuildFilterGroup(group, depth),
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

    private Func<IReadOnlyDictionary<string, object?>, bool> BuildExpressionCondition(FilterExpressionCondition condition)
    {
        var left = CompileRowExpression(condition.Left);

        if (condition.Operator == FilterOperator.IsNull)
        {
            return row => left(row) is null;
        }

        if (condition.Operator == FilterOperator.IsNotNull)
        {
            return row => left(row) is not null;
        }

        var right = CompileRowExpression(condition.Right!);

        return condition.Operator switch
        {
            FilterOperator.Equal => row => FilterEquals(left(row), right(row)),
            FilterOperator.NotEqual => row => FilterNotEquals(left(row), right(row)),
            FilterOperator.LessThan => row => TryFilterCompare(left(row), right(row), out var c) && c < 0,
            FilterOperator.LessThanOrEqual => row => TryFilterCompare(left(row), right(row), out var c) && c <= 0,
            FilterOperator.GreaterThan => row => TryFilterCompare(left(row), right(row), out var c) && c > 0,
            FilterOperator.GreaterThanOrEqual => row => TryFilterCompare(left(row), right(row), out var c) && c >= 0,
            _ => throw new ArgumentException($"Filter expression condition does not support operator '{condition.Operator}'.")
        };
    }

    private Func<IReadOnlyDictionary<string, object?>, bool> BuildFilterGroup(FilterGroup group, int depth)
    {
        var parts = group.Items
            .Select(item => BuildFilter(item, depth + 1))
            .ToArray();

        return group.Operator switch
        {
            LogicalOperator.And => parts.Length == 0
                ? static _ => true
                : row => parts.All(part => part(row)),
            LogicalOperator.Or => parts.Length == 0
                ? static _ => true
                : row => parts.Any(part => part(row)),
            LogicalOperator.Not => parts.Length == 1
                ? row => !parts[0](row)
                : throw new ArgumentException("NOT filter group requires exactly one item."),
            _ => throw new ArgumentException($"Unsupported logical operator '{group.Operator}'.")
        };
    }

    private Func<IReadOnlyDictionary<string, object?>, bool> BuildFilterCondition(FilterCondition condition)
    {
        var field = ResolveField(condition.Field);

        if (condition.Operator is FilterOperator.IsNull)
        {
            return row => field.Accessor(row) is null;
        }

        if (condition.Operator is FilterOperator.IsNotNull)
        {
            return row => field.Accessor(row) is not null;
        }

        if (condition.Operator is FilterOperator.In or FilterOperator.NotIn)
        {
            return BuildInPredicate(field, condition, negate: condition.Operator == FilterOperator.NotIn);
        }

        if (condition.Value is null)
        {
            throw new ArgumentException($"Operator '{condition.Operator}' requires 'value'.");
        }

        if (condition.Value is FieldRefValue fieldRef)
        {
            var rightField = ResolveField(fieldRef.Name);
            return condition.Operator switch
            {
                FilterOperator.Equal => row => FilterEquals(field.Accessor(row), rightField.Accessor(row)),
                FilterOperator.NotEqual => row => FilterNotEquals(field.Accessor(row), rightField.Accessor(row)),
                FilterOperator.LessThan => row => TryFilterCompare(field.Accessor(row), rightField.Accessor(row), out var c) && c < 0,
                FilterOperator.LessThanOrEqual => row => TryFilterCompare(field.Accessor(row), rightField.Accessor(row), out var c) && c <= 0,
                FilterOperator.GreaterThan => row => TryFilterCompare(field.Accessor(row), rightField.Accessor(row), out var c) && c > 0,
                FilterOperator.GreaterThanOrEqual => row => TryFilterCompare(field.Accessor(row), rightField.Accessor(row), out var c) && c >= 0,
                _ => throw new ArgumentException($"Operator '{condition.Operator}' does not support field reference values.")
            };
        }

        var value = ResolveValue(condition.Value);

        return condition.Operator switch
        {
            FilterOperator.Equal => row => FilterEquals(field.Accessor(row), value),
            FilterOperator.NotEqual => row => FilterNotEquals(field.Accessor(row), value),
            FilterOperator.LessThan => row => TryFilterCompare(field.Accessor(row), value, out var c) && c < 0,
            FilterOperator.LessThanOrEqual => row => TryFilterCompare(field.Accessor(row), value, out var c) && c <= 0,
            FilterOperator.GreaterThan => row => TryFilterCompare(field.Accessor(row), value, out var c) && c > 0,
            FilterOperator.GreaterThanOrEqual => row => TryFilterCompare(field.Accessor(row), value, out var c) && c >= 0,
            FilterOperator.Like => row => IsLike(field.Accessor(row), RequireStringValue(value, "LIKE")),
            FilterOperator.NotLike => row => field.Accessor(row) is not null && !IsLike(field.Accessor(row), RequireStringValue(value, "NOT_LIKE")),
            _ => throw new ArgumentException($"Unsupported filter operator '{condition.Operator}'.")
        };
    }

    private Func<IReadOnlyDictionary<string, object?>, bool> BuildInPredicate(InMemoryQueryField field, FilterCondition condition, bool negate)
    {
        if (condition.Value is not object[] values)
        {
            throw new ArgumentException("IN and NOT_IN operators require array 'value'.");
        }

        if (values.Length == 0)
        {
            return negate ? static _ => true : static _ => false;
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

        return row =>
        {
            var rowValue = field.Accessor(row);
            if (rowValue is null)
            {
                return false;
            }

            var matches = values.Any(candidate => FilterEquals(rowValue, candidate));
            return negate ? !matches : matches;
        };
    }

    private static string RequireStringValue(object? value, string operatorName)
    {
        return value as string ?? throw new ArgumentException($"Operator '{operatorName}' requires a string 'value'.");
    }

    // ----- Result expressions (non-aggregate) -----

    private object? EvaluateScalar(ExpressionNode node, IReadOnlyDictionary<string, object?> row)
    {
        return node switch
        {
            ValueExpression value => ResolveValue(value.Value),
            FieldExpression field => ResolveField(field.Field).Accessor(row),
            NowExpression now => RelativeDateResolver.ResolveNow(now.Anchor, queryNow, queryTimeZone),
            DateDiffExpression dateDiff => EvaluateDateDiff(dateDiff.Unit, EvaluateScalar(dateDiff.Start, row), EvaluateScalar(dateDiff.End, row)),
            AggregateExpression => throw new ArgumentException("Aggregate expressions require at least one aggregate result expression."),
            MathExpression math => EvaluateMath(EvaluateScalar(math.Left, row), EvaluateScalar(math.Right, row), math.Operator),
            _ => throw new ArgumentException($"Unsupported expression type '{node.GetType().Name}'.")
        };
    }

    // ----- Result expressions (aggregate / group by) -----

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

    private void CollectGroupKeys(ExpressionNode node, List<string> groupKeyFields)
    {
        switch (node)
        {
            case FieldExpression field:
                AddGroupKey(groupKeyFields, ResolveField(field.Field).Name);
                break;
            case AggregateExpression:
                break;
            case MathExpression math:
                CollectGroupKeys(math.Left, groupKeyFields);
                CollectGroupKeys(math.Right, groupKeyFields);
                break;
            case DateDiffExpression dateDiff:
                CollectGroupKeys(dateDiff.Start, groupKeyFields);
                CollectGroupKeys(dateDiff.End, groupKeyFields);
                break;
            case ValueExpression or NowExpression:
                break;
            default:
                throw new ArgumentException($"Unsupported expression type '{node.GetType().Name}'.");
        }
    }

    private static void AddGroupKey(List<string> groupKeyFields, string name)
    {
        if (!groupKeyFields.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            groupKeyFields.Add(name);
        }
    }

    private List<QueryRowGroup> GroupRows(List<IReadOnlyDictionary<string, object?>> rows, List<string> groupKeyFields)
    {
        if (groupKeyFields.Count == 0)
        {
            return [new QueryRowGroup(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase), rows)];
        }

        var order = new List<string>();
        var groups = new Dictionary<string, QueryRowGroup>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var keyValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var fieldName in groupKeyFields)
            {
                keyValues[fieldName] = ResolveField(fieldName).Accessor(row);
            }

            var keyString = string.Concat(keyValues.Values.Select(ValueKey));

            if (!groups.TryGetValue(keyString, out var group))
            {
                group = new QueryRowGroup(keyValues, []);
                groups[keyString] = group;
                order.Add(keyString);
            }

            group.Rows.Add(row);
        }

        return [.. order.Select(key => groups[key])];
    }

    private object? EvaluateGroupExpression(ExpressionNode node, QueryRowGroup group)
    {
        return node switch
        {
            ValueExpression value => ResolveValue(value.Value),
            FieldExpression field => group.Key.TryGetValue(ResolveField(field.Field).Name, out var keyValue) ? keyValue : null,
            NowExpression now => RelativeDateResolver.ResolveNow(now.Anchor, queryNow, queryTimeZone),
            DateDiffExpression dateDiff => EvaluateDateDiff(dateDiff.Unit, EvaluateGroupExpression(dateDiff.Start, group), EvaluateGroupExpression(dateDiff.End, group)),
            AggregateExpression aggregate => EvaluateAggregateExpression(aggregate, group),
            MathExpression math => EvaluateMath(EvaluateGroupExpression(math.Left, group), EvaluateGroupExpression(math.Right, group), math.Operator),
            _ => throw new ArgumentException($"Unsupported expression type '{node.GetType().Name}'.")
        };
    }

    /// <summary>
    /// Compiles the filter and the operand of every aggregate in <paramref name="node"/> once, before
    /// any row is grouped.
    /// </summary>
    private void PrepareAggregates(ExpressionNode node)
    {
        switch (node)
        {
            case AggregateExpression aggregate:
                if (aggregate.Filter is not null && !aggregateFilters.ContainsKey(aggregate))
                {
                    aggregateFilters[aggregate] = BuildFilter(aggregate.Filter, 0);
                }

                if (!aggregateOperands.ContainsKey(aggregate))
                {
                    aggregateOperands[aggregate] = CompileOperand(aggregate);
                }

                break;
            case MathExpression math:
                PrepareAggregates(math.Left);
                PrepareAggregates(math.Right);
                break;
        }
    }

    /// <summary>
    /// The per-row value an aggregate folds: its field's accessor, or its <c>expression</c> compiled
    /// into one. <see cref="AggregateOperand.Selector"/> is <see langword="null"/> when the node names
    /// neither, which only <c>count</c> and <c>exists</c> accept.
    /// </summary>
    private AggregateOperand CompileOperand(AggregateExpression node)
    {
        if (node.Expression is not null)
        {
            return new AggregateOperand(CompileRowExpression(node.Expression), $"Aggregate '{node.Action}' expression");
        }

        if (string.IsNullOrWhiteSpace(node.Field))
        {
            return QueryExpressionRules.RequiresOperand(node.Action)
                ? throw QueryExpressionRules.MissingOperand(node)
                : new AggregateOperand(null, string.Empty);
        }

        return new AggregateOperand(ResolveField(node.Field).Accessor, $"Field '{node.Field}'");
    }

    /// <summary>
    /// Compiles an aggregate's <c>expression</c> into a per-row function, with the row-level math of
    /// <see cref="EvaluateScalar"/>: a division by zero is the same null, which the aggregate then
    /// skips like any other null operand.
    /// </summary>
    private Func<IReadOnlyDictionary<string, object?>, object?> CompileRowExpression(ExpressionNode node)
    {
        switch (node)
        {
            case ValueExpression value:
                var constant = ResolveValue(value.Value);
                return _ => constant;
            case FieldExpression field:
                return ResolveField(field.Field).Accessor;
            case NowExpression now:
                var nowConstant = RelativeDateResolver.ResolveNow(now.Anchor, queryNow, queryTimeZone);
                return _ => nowConstant;
            case DateDiffExpression dateDiff:
                var unit = RelativeDateResolver.NormalizeUnit(dateDiff.Unit, "dateDiff unit");
                var startFn = CompileRowExpression(dateDiff.Start);
                var endFn = CompileRowExpression(dateDiff.End);
                return row => EvaluateDateDiff(unit, startFn(row), endFn(row));
            case MathExpression math:
                var left = CompileRowExpression(math.Left);
                var right = CompileRowExpression(math.Right);
                var op = math.Operator;
                return row => EvaluateMath(left(row), right(row), op);
            case AggregateExpression:
                // Already refused by QueryExpressionRules; kept so the switch is total.
                throw new ArgumentException("An aggregate cannot appear inside an aggregate's 'expression'.");
            default:
                throw new ArgumentException($"Unsupported expression type '{node.GetType().Name}'.");
        }
    }

    private object? EvaluateAggregateExpression(AggregateExpression node, QueryRowGroup group)
    {
        aggregateFilters.TryGetValue(node, out var rowFilter);
        var operand = aggregateOperands[node];
        var rows = rowFilter is null ? group.Rows : group.Rows.Where(rowFilter);

        return node.Action switch
        {
            AggregateAction.Sum => SumValues(rows, operand),
            AggregateAction.Avg => AvgValues(rows, operand),
            AggregateAction.Min => MinMaxValues(rows, operand, min: true),
            AggregateAction.Max => MinMaxValues(rows, operand, min: false),
            AggregateAction.Count => CountValues(rows, operand),
            AggregateAction.CountDistinct => CountDistinctValues(rows, operand),
            AggregateAction.Exists => CountValues(rows, operand) > 0,
            _ => throw new ArgumentException($"Unsupported aggregate action '{node.Action}'.")
        };
    }

    private static object? SumValues(IEnumerable<IReadOnlyDictionary<string, object?>> rows, AggregateOperand operand)
    {
        var selector = operand.Require();
        decimal sum = 0;
        var any = false;

        foreach (var row in rows)
        {
            var value = selector(row);
            if (value is null)
            {
                continue;
            }

            if (!TryToDecimal(value, out var numericValue))
            {
                throw new ArgumentException($"{operand.Label} value is not numeric.");
            }

            sum += numericValue;
            any = true;
        }

        return any ? sum : null;
    }

    private static object? AvgValues(IEnumerable<IReadOnlyDictionary<string, object?>> rows, AggregateOperand operand)
    {
        var selector = operand.Require();
        decimal sum = 0;
        var count = 0;

        foreach (var row in rows)
        {
            var value = selector(row);
            if (value is null)
            {
                continue;
            }

            if (!TryToDecimal(value, out var numericValue))
            {
                throw new ArgumentException($"{operand.Label} value is not numeric.");
            }

            sum += numericValue;
            count++;
        }

        return count == 0 ? null : sum / count;
    }

    private static object? MinMaxValues(IEnumerable<IReadOnlyDictionary<string, object?>> rows, AggregateOperand operand, bool min)
    {
        var selector = operand.Require();
        object? best = null;
        var any = false;

        foreach (var row in rows)
        {
            var value = selector(row);
            if (value is null)
            {
                continue;
            }

            if (!any || (min ? CompareNonNull(value, best!) < 0 : CompareNonNull(value, best!) > 0))
            {
                best = value;
                any = true;
            }
        }

        return any ? best : null;
    }

    private static int CountValues(IEnumerable<IReadOnlyDictionary<string, object?>> rows, AggregateOperand operand)
    {
        var selector = operand.Selector;
        return selector is null
            ? rows.Count()
            : rows.Count(row => selector(row) is not null);
    }

    private static int CountDistinctValues(IEnumerable<IReadOnlyDictionary<string, object?>> rows, AggregateOperand operand)
    {
        var selector = operand.Require();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var value = selector(row);
            if (value is null)
            {
                continue;
            }

            seen.Add(ValueKey(value));
        }

        return seen.Count;
    }

    /// <summary>
    /// The arithmetic of a <c>math</c> node, over rows and over aggregates alike. A non-numeric
    /// operand and a division by zero are both <see langword="null"/>, not an error (canonical
    /// contract) — the same NULL <c>QuerySqlBuilder</c> gets from <c>NULLIF(divisor, 0)</c>.
    /// </summary>
    private static object? EvaluateMath(object? left, object? right, MathOperator op)
    {
        if (!TryToDecimal(left, out var leftValue) || !TryToDecimal(right, out var rightValue))
        {
            return null;
        }

        return op switch
        {
            MathOperator.Add => leftValue + rightValue,
            MathOperator.Subtract => leftValue - rightValue,
            MathOperator.Multiply => leftValue * rightValue,
            MathOperator.Divide => rightValue == 0 ? null : leftValue / rightValue,
            _ => throw new ArgumentException($"Unsupported math operator '{op}'.")
        };
    }

    // ----- Ordering -----

    private List<IReadOnlyDictionary<string, object?>> ApplyOrder(List<IReadOnlyDictionary<string, object?>> rows, QueryRequest? query)
    {
        var order = query?.Order ?? [];
        if (order.Count == 0)
        {
            return rows;
        }

        if (order.Count > MaxOrderItems)
        {
            throw new ArgumentException($"Too many order items. Max allowed: {MaxOrderItems}.");
        }

        IOrderedEnumerable<IReadOnlyDictionary<string, object?>>? ordered = null;

        foreach (var item in order)
        {
            if (string.IsNullOrWhiteSpace(item.Field))
            {
                throw new ArgumentException("Order field is required.");
            }

            var field = ResolveField(item.Field);
            var descending = item.Direction.Trim().ToUpperInvariant() switch
            {
                "ASC" => false,
                "DESC" => true,
                _ => throw new ArgumentException($"Unsupported order direction '{item.Direction}'.")
            };

            object? KeySelector(IReadOnlyDictionary<string, object?> row) => field.Accessor(row);

            ordered = ordered is null
                ? (descending ? rows.OrderByDescending(KeySelector, OrderComparer.Instance) : rows.OrderBy(KeySelector, OrderComparer.Instance))
                : (descending ? ordered.ThenByDescending(KeySelector, OrderComparer.Instance) : ordered.ThenBy(KeySelector, OrderComparer.Instance));
        }

        return ordered!.ToList();
    }

    // ----- Value helpers -----

    private DateTimeOffset queryNow;
    private TimeZoneInfo? queryTimeZone;
    private DayOfWeek queryWeekStart = DayOfWeek.Monday;

    private object? ResolveValue(object? value) => value switch
    {
        RelativeDateValue relativeDate => RelativeDateResolver.Resolve(relativeDate, queryNow, queryTimeZone, queryWeekStart),
        FieldRefValue => throw new ArgumentException("Field reference values are only supported on filter comparison conditions."),
        _ => value
    };

    private object? EvaluateDateDiff(string unit, object? startValue, object? endValue)
    {
        if (!TryToZoneDateTime(startValue, out var startDt) || !TryToZoneDateTime(endValue, out var endDt))
        {
            return null;
        }

        return RelativeDateResolver.ComputeDateDiff(unit, startDt, endDt, queryWeekStart);
    }

    private bool TryToZoneDateTime(object? value, out DateTime result)
    {
        if (value is DateTimeOffset dto)
        {
            result = queryTimeZone is null
                ? dto.LocalDateTime
                : TimeZoneInfo.ConvertTime(dto, queryTimeZone).DateTime;
            return true;
        }

        return TryToDateTime(value, out result);
    }

    private static bool FilterEquals(object? a, object? b) => a is not null && b is not null && CompareNonNull(a, b) == 0;

    private static bool FilterNotEquals(object? a, object? b) => a is not null && b is not null && CompareNonNull(a, b) != 0;

    private static bool TryFilterCompare(object? a, object? b, out int comparison)
    {
        if (a is null || b is null)
        {
            comparison = 0;
            return false;
        }

        comparison = CompareNonNull(a, b);
        return true;
    }

    private static bool IsLike(object? value, string pattern)
    {
        if (value is null)
        {
            return false;
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        var escaped = Regex.Escape(pattern)
            .Replace("%", ".*", StringComparison.Ordinal)
            .Replace("_", ".", StringComparison.Ordinal);

        return Regex.IsMatch(text, $"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
    }

    private static int CompareNonNull(object a, object b)
    {
        if (TryToDecimal(a, out var da) && TryToDecimal(b, out var db))
        {
            return da.CompareTo(db);
        }

        if (TryToDateTime(a, out var ta) && TryToDateTime(b, out var tb))
        {
            return ta.CompareTo(tb);
        }

        if (a is bool ba && b is bool bb)
        {
            return ba.CompareTo(bb);
        }

        return string.Compare(
            Convert.ToString(a, CultureInfo.InvariantCulture),
            Convert.ToString(b, CultureInfo.InvariantCulture),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryToDecimal(object? value, out decimal result)
    {
        switch (value)
        {
            case decimal d:
                result = d;
                return true;
            case int i:
                result = i;
                return true;
            case long l:
                result = l;
                return true;
            case double db:
                result = (decimal)db;
                return true;
            case float f:
                result = (decimal)f;
                return true;
            case string s when decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed):
                result = parsed;
                return true;
            default:
                result = default;
                return false;
        }
    }

    private static bool TryToDateTime(object? value, out DateTime result)
    {
        switch (value)
        {
            case DateTime dt:
                result = dt;
                return true;
            case DateOnly d:
                result = d.ToDateTime(TimeOnly.MinValue);
                return true;
            case DateTimeOffset dto:
                result = dto.LocalDateTime;
                return true;
            case string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed):
                result = parsed;
                return true;
            default:
                result = default;
                return false;
        }
    }

    /// <remarks>
    /// A decimal's scale is normalised first: <c>1.50 * 2</c> is <c>3.00</c> and <c>3 * 1</c> is
    /// <c>3</c>, the same number, and a <c>countDistinct</c> over an expression must count it once —
    /// as <c>COUNT(DISTINCT …)</c> does on the SQL path.
    /// </remarks>
    private static string ValueKey(object? value)
    {
        return value switch
        {
            null => " null",
            decimal number => $"Decimal:{(number / 1.000000000000000000000000000000000m).ToString(CultureInfo.InvariantCulture)}",
            _ => $"{value.GetType().Name}:{Convert.ToString(value, CultureInfo.InvariantCulture)}"
        };
    }

    private InMemoryQueryField ResolveField(string name)
    {
        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName) || !fields.TryGetValue(normalizedName, out var field))
        {
            throw new ArgumentException($"Field '{name}' is not allowed. Allowed fields: {string.Join(", ", fields.Keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase))}.");
        }

        return field;
    }

    /// <summary>
    /// What an aggregate folds per row, and how an error about it names it.
    /// </summary>
    /// <param name="Selector">The per-row value; <see langword="null"/> for a fieldless
    /// <c>count</c>/<c>exists</c>, which counts rows instead.</param>
    /// <param name="Label">"Field 'x'" or "Aggregate 'Sum' expression", for the not-numeric error.</param>
    private sealed record AggregateOperand(Func<IReadOnlyDictionary<string, object?>, object?>? Selector, string Label)
    {
        public Func<IReadOnlyDictionary<string, object?>, object?> Require() =>
            Selector ?? throw new InvalidOperationException("Aggregate operand was not validated.");
    }

    private sealed record QueryRowGroup(Dictionary<string, object?> Key, List<IReadOnlyDictionary<string, object?>> Rows);

    private sealed class OrderComparer : IComparer<object?>
    {
        public static readonly OrderComparer Instance = new();

        public int Compare(object? x, object? y)
        {
            if (x is null && y is null)
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            return CompareNonNull(x, y);
        }
    }
}
