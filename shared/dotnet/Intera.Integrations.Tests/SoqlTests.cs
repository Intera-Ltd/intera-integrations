using Intera.Common.Models.Query;
using Intera.Salesforce.Api.Infrastructure.Soql;
using Intera.Salesforce.Api.Models.Query;
using Xunit;

namespace Intera.Integrations.Tests;

public sealed class SoqlTests
{
    [Fact]
    public void SoqlLiteral_EscapesQuotesBackslashesAndControlCharacters()
    {
        var textField = SoqlField.Text("account_name", "Name");
        var rendered = SoqlLiteral.Render(textField, "O'Reilly \\ \"Corp\"\nLine2\tTab", TimeZoneInfo.Utc);

        Assert.Equal("'O\\'Reilly \\\\ \\\"Corp\\\"\\nLine2\\tTab'", rendered);

        var like = SoqlLiteral.RenderLikePattern("%50%_off'\\test%");
        Assert.Equal("'%50%_off\\'\\\\test%'", like);
    }

    [Fact]
    public void SoqlQueryBuilder_RendersRelativeDatesAndNormalizesSingleFieldExpressionFilters()
    {
        var query = new QueryRequest
        {
            TimeZone = "UTC",
            Filter = new FilterExpressionCondition
            {
                Type = "expression",
                Left = new FieldExpression { Type = "field", Field = "amount" },
                Operator = FilterOperator.GreaterThanOrEqual,
                Right = new ValueExpression { Type = "value", Value = 1500 }
            },
            Filters =
            [
                new FilterCondition
                {
                    Type = "condition",
                    Field = "close_date",
                    Operator = FilterOperator.GreaterThanOrEqual,
                    Value = new RelativeDateValue
                    {
                        Type = "relativeDate",
                        From = "today",
                        Unit = "month",
                        Amount = 0,
                        SnapTo = "month",
                        SnapEdge = "start"
                    }
                }
            ],
            Order = [new QueryOrder { Field = "amount", Direction = "DESC" }],
            Top = 25
        };

        var built = new SoqlQueryBuilder(OpportunityQueryFields.Source).Build(query, defaultLimit: 2000);

        Assert.Contains("FROM Opportunity", built.Soql, StringComparison.Ordinal);
        Assert.Contains("(Amount >= 1500)", built.Soql, StringComparison.Ordinal);
        Assert.Matches(@"CloseDate >= \d{4}-\d{2}-01\)", built.Soql);
        Assert.EndsWith("ORDER BY Amount DESC LIMIT 25", built.Soql, StringComparison.Ordinal);
    }

    [Fact]
    public void SoqlQueryBuilder_RejectsFieldToFieldComparisonsAndMultiFieldExpressionFilters()
    {
        var fieldComparison = new QueryRequest
        {
            Filter = new FilterCondition
            {
                Type = "condition",
                Field = "amount",
                Operator = FilterOperator.GreaterThan,
                Value = new FieldRefValue { Type = "field", Name = "expected_revenue" }
            }
        };

        var fieldEx = Assert.Throws<ArgumentException>(
            () => new SoqlQueryBuilder(OpportunityQueryFields.Source).Build(fieldComparison, defaultLimit: 100));
        Assert.Contains("Field-to-field comparisons", fieldEx.Message, StringComparison.Ordinal);

        var multiFieldExpr = new QueryRequest
        {
            Filter = new FilterExpressionCondition
            {
                Type = "expression",
                Left = new MathExpression
                {
                    Type = "math",
                    Operator = MathOperator.Subtract,
                    Left = new FieldExpression { Type = "field", Field = "amount" },
                    Right = new FieldExpression { Type = "field", Field = "expected_revenue" }
                },
                Operator = FilterOperator.GreaterThan,
                Right = new ValueExpression { Type = "value", Value = 0 }
            }
        };

        var exprEx = Assert.Throws<ArgumentException>(
            () => new SoqlQueryBuilder(OpportunityQueryFields.Source).Build(multiFieldExpr, defaultLimit: 100));
        Assert.Contains("Formula filter conditions", exprEx.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SoqlQueryBuilder_RejectsGroupingByCurrencyMeasure()
    {
        var query = new QueryRequest
        {
            Result = new QueryResult
            {
                Expressions =
                [
                    new ResultExpression
                    {
                        Alias = "amt",
                        Expression = new FieldExpression { Type = "field", Field = "amount" }
                    },
                    new ResultExpression
                    {
                        Alias = "cnt",
                        Expression = new AggregateExpression { Type = "aggregate", Action = AggregateAction.Count }
                    }
                ]
            }
        };

        var ex = Assert.Throws<ArgumentException>(
            () => new SoqlQueryBuilder(OpportunityQueryFields.Source).Build(query, defaultLimit: 100));
        Assert.Contains("GROUP BY", ex.Message, StringComparison.Ordinal);
    }
}
