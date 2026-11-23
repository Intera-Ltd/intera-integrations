using Intera.Common.Models.Query;

namespace Intera.Common.Infrastructure.Query;

/// <summary>
/// Shape rules of a result or row expression tree that do not depend on which engine runs it: how deep
/// it may nest, and what an aggregate's or row condition's operand may be.
/// </summary>
public static class QueryExpressionRules
{
    public const int MaxExpressionDepth = 10;

    public static void Validate(ExpressionNode node)
    {
        Validate(node, depth: 0, insideAggregate: false);
    }

    /// <summary>
    /// Validates a row-scope expression (inside a <see cref="FilterExpressionCondition"/>), enforcing
    /// depth limits, valid <c>now</c>/<c>dateDiff</c> arguments, and forbidding aggregates.
    /// </summary>
    public static void ValidateRowScope(ExpressionNode node)
    {
        Validate(node, depth: 0, insideAggregate: true);
    }

    public static bool RequiresOperand(AggregateAction action)
    {
        return action is not (AggregateAction.Count or AggregateAction.Exists);
    }

    public static ArgumentException MissingOperand(AggregateExpression node)
    {
        return new ArgumentException($"Aggregate action '{node.Action}' requires 'field' or 'expression'.");
    }

    private static void Validate(ExpressionNode node, int depth, bool insideAggregate)
    {
        if (depth > MaxExpressionDepth)
        {
            throw new ArgumentException($"Expression is too deep. Max allowed depth: {MaxExpressionDepth}.");
        }

        switch (node)
        {
            case ValueExpression or FieldExpression:
                return;
            case NowExpression now:
                ValidateNow(now);
                return;
            case DateDiffExpression dateDiff:
                _ = RelativeDateResolver.NormalizeUnit(dateDiff.Unit, "dateDiff unit");
                Validate(dateDiff.Start, depth + 1, insideAggregate: true);
                Validate(dateDiff.End, depth + 1, insideAggregate: true);
                return;
            case MathExpression math:
                Validate(math.Left, depth + 1, insideAggregate);
                Validate(math.Right, depth + 1, insideAggregate);
                return;
            case AggregateExpression aggregate:
                if (insideAggregate)
                {
                    throw new ArgumentException($"Aggregate action '{aggregate.Action}' cannot appear inside an aggregate's 'expression': the expression is evaluated per row.");
                }

                ValidateOperand(aggregate);

                if (aggregate.Expression is not null)
                {
                    Validate(aggregate.Expression, depth + 1, insideAggregate: true);
                }

                return;
            default:
                throw new ArgumentException($"Unsupported expression type '{node.GetType().Name}'.");
        }
    }

    private static void ValidateNow(NowExpression node)
    {
        if (!string.IsNullOrWhiteSpace(node.Anchor)
            && !string.Equals(node.Anchor.Trim(), "now", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(node.Anchor.Trim(), "today", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unsupported now anchor '{node.Anchor}'. Expected 'now' or 'today'.");
        }
    }

    private static void ValidateOperand(AggregateExpression node)
    {
        var hasField = !string.IsNullOrWhiteSpace(node.Field);

        if (hasField && node.Expression is not null)
        {
            throw new ArgumentException($"Aggregate action '{node.Action}' takes either 'field' or 'expression', not both.");
        }

        if (!hasField && node.Expression is null && RequiresOperand(node.Action))
        {
            throw MissingOperand(node);
        }
    }
}
