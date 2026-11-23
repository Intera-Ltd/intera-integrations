using Intera.Common.Models.Query;

namespace Intera.Jira.Api.Infrastructure;

/// <summary>
/// Which fields a query touches — in its filter, its expressions (aggregates, their per-row filters and
/// expressions, math), its order and its explicit grouping — and whether it asks for rows as they are.
/// </summary>
/// <remarks>
/// Used to fetch only what a query needs from Jira: a custom field costs payload on every issue of a search, and a
/// board's filter costs one more call per board, so neither is read for a query that never mentions it. A
/// passthrough query (no <c>result</c>) mentions nothing and needs everything.
/// </remarks>
public sealed class QueryFieldReferences
{
    private readonly HashSet<string> fields = new(StringComparer.OrdinalIgnoreCase);

    private QueryFieldReferences(bool passthrough)
    {
        IsPassthrough = passthrough;
    }

    /// <summary>The query returns whole rows, so every column may be shown.</summary>
    public bool IsPassthrough { get; }

    public IReadOnlyCollection<string> Fields => fields;

    /// <summary>Whether the answer may depend on <paramref name="field"/>.</summary>
    public bool Needs(string field) => IsPassthrough || fields.Contains(field);

    public static QueryFieldReferences Of(QueryRequest? query)
    {
        var references = new QueryFieldReferences(query?.Result is null || query.Result.Expressions.Count == 0);
        if (query is null)
        {
            return references;
        }

        references.Visit(query.Filter);
        foreach (var filter in query.Filters)
        {
            references.Visit(filter);
        }

        foreach (var order in query.Order)
        {
            references.fields.Add(order.Field);
        }

        if (query.Result is { } result)
        {
            foreach (var key in result.GroupBy)
            {
                references.fields.Add(key);
            }

            foreach (var expression in result.Expressions)
            {
                references.Visit(expression.Expression);
            }
        }

        return references;
    }

    private void Visit(FilterNode? node)
    {
        switch (node)
        {
            case FilterCondition condition:
                fields.Add(condition.Field);
                if (condition.Value is FieldRefValue fieldRef)
                {
                    fields.Add(fieldRef.Name);
                }

                break;
            case FilterExpressionCondition expr:
                Visit(expr.Left);
                Visit(expr.Right);
                break;
            case FilterGroup group:
                foreach (var item in group.Items)
                {
                    Visit(item);
                }

                break;
        }
    }

    private void Visit(ExpressionNode? node)
    {
        switch (node)
        {
            case FieldExpression field:
                fields.Add(field.Field);
                break;
            case DateDiffExpression dateDiff:
                Visit(dateDiff.Start);
                Visit(dateDiff.End);
                break;
            case AggregateExpression aggregate:
                if (aggregate.Field is { } name)
                {
                    fields.Add(name);
                }

                Visit(aggregate.Expression);
                Visit(aggregate.Filter);
                break;
            case MathExpression math:
                Visit(math.Left);
                Visit(math.Right);
                break;
        }
    }
}
