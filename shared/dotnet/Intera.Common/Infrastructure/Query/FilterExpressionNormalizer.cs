using Intera.Common.Models.Query;

namespace Intera.Common.Infrastructure.Query;

/// <summary>
/// Algebraically rewrites single-field <see cref="FilterExpressionCondition"/> nodes into canonical
/// SARGable <see cref="FilterCondition"/> or <see cref="FilterGroup"/> nodes, matching the INTERA
/// query contract normalizer.
/// </summary>
public static class FilterExpressionNormalizer
{
    public static FilterNode? TryNormalize(FilterExpressionCondition node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Operator is FilterOperator.IsNull or FilterOperator.IsNotNull)
        {
            return node.Left is FieldExpression unaryField
                ? new FilterCondition { Type = "condition", Field = unaryField.Field, Operator = node.Operator }
                : null;
        }

        if (node.Right is null)
        {
            return null;
        }

        var op = node.Operator;

        // `field <op> field`
        if (node.Left is FieldExpression leftField && node.Right is FieldExpression rightField)
        {
            return new FilterCondition
            {
                Type = "condition",
                Field = leftField.Field,
                Operator = op,
                Value = new FieldRefValue { Type = "field", Name = rightField.Field }
            };
        }

        // `field <op> literal` or `literal <op> field`
        if (node.Left is FieldExpression lf && node.Right is ValueExpression rv)
        {
            return BuildFieldVsLiteral(lf.Field, op, rv.Value);
        }

        if (node.Left is ValueExpression lv && node.Right is FieldExpression rf)
        {
            return BuildFieldVsLiteral(rf.Field, Flip(op), lv.Value);
        }

        // `field <op> now` or `now <op> field`
        if (node.Left is FieldExpression fnLeft && node.Right is NowExpression nowRight)
        {
            return SingleCondition(fnLeft.Field, op, AnchorAsRelativeDate(nowRight));
        }

        if (node.Left is NowExpression nowLeft && node.Right is FieldExpression fnRight)
        {
            return SingleCondition(fnRight.Field, Flip(op), AnchorAsRelativeDate(nowLeft));
        }

        // `dateDiff(...) <op> K` or `K <op> dateDiff(...)`
        if (node.Left is DateDiffExpression leftDiff && TryReadInt32(node.Right, out var rightK))
        {
            return NormalizeDateDiffAgainstNow(leftDiff, op, rightK);
        }

        if (node.Right is DateDiffExpression rightDiff && TryReadInt32(node.Left, out var leftK))
        {
            return NormalizeDateDiffAgainstNow(rightDiff, Flip(op), leftK);
        }

        return null;
    }

    private static FilterNode? BuildFieldVsLiteral(string field, FilterOperator op, object? value)
    {
        if (value is null)
        {
            return op switch
            {
                FilterOperator.Equal => new FilterCondition { Type = "condition", Field = field, Operator = FilterOperator.IsNull },
                FilterOperator.NotEqual => new FilterCondition { Type = "condition", Field = field, Operator = FilterOperator.IsNotNull },
                _ => null
            };
        }

        return new FilterCondition
        {
            Type = "condition",
            Field = field,
            Operator = op,
            Value = value
        };
    }

    private static FilterNode? NormalizeDateDiffAgainstNow(DateDiffExpression diff, FilterOperator op, int k)
    {
        var unit = RelativeDateResolver.NormalizeUnit(diff.Unit, "dateDiff unit");

        // Case A: `dateDiff(unit, field(F), now(anchor)) <op> K`
        if (diff.Start is FieldExpression ageField && diff.End is NowExpression ageNow)
        {
            var anchor = NormalizeAnchor(ageNow.Anchor);
            RelativeDateValue B(int shift) => BucketBoundary(unit, anchor, shift);

            return op switch
            {
                FilterOperator.GreaterThan => SingleCondition(ageField.Field, FilterOperator.LessThan, B(-k)),
                FilterOperator.GreaterThanOrEqual => SingleCondition(ageField.Field, FilterOperator.LessThan, B(1 - k)),
                FilterOperator.LessThanOrEqual => SingleCondition(ageField.Field, FilterOperator.GreaterThanOrEqual, B(-k)),
                FilterOperator.LessThan => SingleCondition(ageField.Field, FilterOperator.GreaterThanOrEqual, B(1 - k)),
                FilterOperator.Equal => new FilterGroup
                {
                    Type = "group",
                    Operator = LogicalOperator.And,
                    Items =
                    [
                        SingleCondition(ageField.Field, FilterOperator.GreaterThanOrEqual, B(-k)),
                        SingleCondition(ageField.Field, FilterOperator.LessThan, B(1 - k))
                    ]
                },
                FilterOperator.NotEqual => new FilterGroup
                {
                    Type = "group",
                    Operator = LogicalOperator.Or,
                    Items =
                    [
                        SingleCondition(ageField.Field, FilterOperator.LessThan, B(-k)),
                        SingleCondition(ageField.Field, FilterOperator.GreaterThanOrEqual, B(1 - k))
                    ]
                },
                _ => null
            };
        }

        // Case B: `dateDiff(unit, now(anchor), field(F)) <op> K`
        if (diff.Start is NowExpression horizonNow && diff.End is FieldExpression horizonField)
        {
            var anchor = NormalizeAnchor(horizonNow.Anchor);
            RelativeDateValue B(int shift) => BucketBoundary(unit, anchor, shift);

            return op switch
            {
                FilterOperator.GreaterThanOrEqual => SingleCondition(horizonField.Field, FilterOperator.GreaterThanOrEqual, B(k)),
                FilterOperator.GreaterThan => SingleCondition(horizonField.Field, FilterOperator.GreaterThanOrEqual, B(k + 1)),
                FilterOperator.LessThan => SingleCondition(horizonField.Field, FilterOperator.LessThan, B(k)),
                FilterOperator.LessThanOrEqual => SingleCondition(horizonField.Field, FilterOperator.LessThan, B(k + 1)),
                FilterOperator.Equal => new FilterGroup
                {
                    Type = "group",
                    Operator = LogicalOperator.And,
                    Items =
                    [
                        SingleCondition(horizonField.Field, FilterOperator.GreaterThanOrEqual, B(k)),
                        SingleCondition(horizonField.Field, FilterOperator.LessThan, B(k + 1))
                    ]
                },
                FilterOperator.NotEqual => new FilterGroup
                {
                    Type = "group",
                    Operator = LogicalOperator.Or,
                    Items =
                    [
                        SingleCondition(horizonField.Field, FilterOperator.LessThan, B(k)),
                        SingleCondition(horizonField.Field, FilterOperator.GreaterThanOrEqual, B(k + 1))
                    ]
                },
                _ => null
            };
        }

        return null;
    }

    private static RelativeDateValue BucketBoundary(string canonicalUnit, string anchor, int shift)
    {
        if (canonicalUnit == "day")
        {
            return new RelativeDateValue
            {
                Type = "relativeDate",
                From = "today",
                Unit = "day",
                Amount = shift
            };
        }

        var isCalendarPeriod = canonicalUnit is "year" or "quarter" or "month" or "week";
        return new RelativeDateValue
        {
            Type = "relativeDate",
            From = isCalendarPeriod ? "today" : anchor,
            Unit = canonicalUnit,
            Amount = shift,
            SnapTo = canonicalUnit
        };
    }

    private static RelativeDateValue AnchorAsRelativeDate(NowExpression node) =>
        new()
        {
            Type = "relativeDate",
            From = NormalizeAnchor(node.Anchor),
            Unit = "day",
            Amount = 0
        };

    private static string NormalizeAnchor(string? anchor)
    {
        if (string.IsNullOrWhiteSpace(anchor)
            || string.Equals(anchor.Trim(), "now", StringComparison.OrdinalIgnoreCase))
        {
            return "now";
        }

        if (string.Equals(anchor.Trim(), "today", StringComparison.OrdinalIgnoreCase))
        {
            return "today";
        }

        throw new ArgumentException($"Unsupported now anchor '{anchor}'. Expected 'now' or 'today'.");
    }

    private static FilterCondition SingleCondition(string field, FilterOperator op, RelativeDateValue value) =>
        new()
        {
            Type = "condition",
            Field = field,
            Operator = op,
            Value = value
        };

    private static FilterOperator Flip(FilterOperator op) =>
        op switch
        {
            FilterOperator.LessThan => FilterOperator.GreaterThan,
            FilterOperator.LessThanOrEqual => FilterOperator.GreaterThanOrEqual,
            FilterOperator.GreaterThan => FilterOperator.LessThan,
            FilterOperator.GreaterThanOrEqual => FilterOperator.LessThanOrEqual,
            _ => op
        };

    private static bool TryReadInt32(ExpressionNode node, out int result)
    {
        if (node is ValueExpression { Value: int i })
        {
            result = i;
            return true;
        }

        if (node is ValueExpression { Value: long l and >= int.MinValue and <= int.MaxValue })
        {
            result = (int)l;
            return true;
        }

        result = 0;
        return false;
    }
}
