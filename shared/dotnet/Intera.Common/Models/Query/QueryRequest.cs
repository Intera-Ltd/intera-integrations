using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Intera.Common.Models.Query;

/// <summary>
/// The query DSL's request body, shared by every service in this solution.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every multi-word property here carries an explicit <see cref="JsonPropertyNameAttribute"/>,
/// and that is load-bearing rather than tidy.</b> <c>InteraApiSetup.AddInteraApi</c> sets
/// <c>PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower</c>, and System.Text.Json applies a
/// naming policy on <b>read</b> as well as on write — so without the attribute the binder expects
/// <c>time_zone</c> / <c>week_start</c> / <c>group_by</c> while the host and every plugin send
/// camelCase, and the property is silently dropped rather than rejected. That was the state until
/// 2026-09-03: an admin-set time zone did nothing, and an explicit grouping key was discarded, both
/// with a 200 and a plausible-looking answer.
/// </para>
/// <para>
/// The nodes inside <see cref="Filter"/> and <see cref="QueryResult.Expressions"/> need no such
/// attribute: <see cref="FilterNodeJsonConverter"/> and <see cref="ExpressionNodeJsonConverter"/>
/// read property names literally, which is why <c>snapTo</c> always worked. So a new multi-word
/// property is safe only inside those trees — anywhere else it needs the attribute, and a single-word
/// one needs nothing.
/// </para>
/// </remarks>
public sealed class QueryRequest
{
    public int? Top { get; init; }

    /// <summary>
    /// Rows to skip before <see cref="Top"/> — the offset half of a page.
    /// </summary>
    /// <remarks>
    /// Setting it switches <c>QuerySqlBuilder</c> from <c>TOP (n)</c> to
    /// <c>ORDER BY … OFFSET n ROWS FETCH NEXT m ROWS ONLY</c>, because SQL Server offers no other
    /// way to skip and the two clauses are mutually exclusive. Consequences worth knowing:
    /// <list type="bullet">
    /// <item>An <see cref="Order"/> is what makes a page reproducible. Without one the builder
    /// emits <c>ORDER BY (SELECT NULL)</c> — valid, but the server is then free to return the
    /// same row on two different pages, so a paging caller should always send an order.</item>
    /// <item>Not supported together with aggregates: <c>ORDER BY</c> already is not, and a page
    /// of a grouped result is a different question from a page of rows.</item>
    /// </list>
    /// </remarks>
    public int? Skip { get; init; }

    /// <summary>
    /// Time zone used to resolve <see cref="RelativeDateValue"/> windows, as a Windows or IANA id
    /// (canonical contract, 2026-08-28). Absent means the API process's local zone.
    /// </summary>
    /// <remarks>
    /// It has to be explicit because the two execution paths used to disagree by construction:
    /// the SQL builder anchored on <c>GETDATE()</c> — the DATABASE server's clock — while the
    /// in-memory engine used <c>DateTime.Now</c>, this process's clock. "Start of month" then
    /// depended on which engine served the query, and nothing said so.
    /// </remarks>
    /// <seealso cref="QueryResult.GroupBy"/>
    [JsonPropertyName("timeZone")]
    public string? TimeZone { get; init; }

    /// <summary>
    /// First day of the week for <c>snapTo: 'week'</c>, as a day name (<c>monday</c>, <c>sunday</c>,
    /// …). Absent means Monday.
    /// </summary>
    /// <remarks>
    /// Configuration rather than a database setting on purpose: SQL Server's <c>DATEFIRST</c> would
    /// make the same filter mean a different week per server, and the correct answer is a property
    /// of the market the instance serves.
    /// </remarks>
    [JsonPropertyName("weekStart")]
    public string? WeekStart { get; init; }

    public FilterNode? Filter { get; init; }

    public IReadOnlyList<FilterNode> Filters { get; init; } = [];

    public IReadOnlyList<QueryOrder> Order { get; init; } = [];

    public QueryResult? Result { get; init; }
}

public sealed class QueryOrder
{
    public required string Field { get; init; }

    public string Direction { get; init; } = "asc";
}

public sealed class QueryResult
{
    public IReadOnlyList<ResultExpression> Expressions { get; init; } = [];

    /// <summary>
    /// Explicit grouping keys (canonical query contract, 2026-08-28). When empty the keys are
    /// inferred as before: every field referenced outside an aggregate is one.
    /// </summary>
    /// <remarks>
    /// The inference alone had two costs. Adding one field to a result silently turned a one-row
    /// answer into N rows — a metric reading the first row then read an arbitrary group — and
    /// "sum per customer, without emitting the customer" could not be expressed at all, because a
    /// key had to appear in the output to exist.
    /// </remarks>
    [JsonPropertyName("groupBy")]
    public IReadOnlyList<string> GroupBy { get; init; } = [];
}

public sealed class ResultExpression
{
    public required string Alias { get; init; }

    public required ExpressionNode Expression { get; init; }
}

[JsonConverter(typeof(FilterNodeJsonConverter))]
public abstract class FilterNode
{
    public required string Type { get; init; }
}

public sealed class FilterCondition : FilterNode
{
    public required string Field { get; init; }

    public required FilterOperator Operator { get; init; }

    public object? Value { get; init; }
}

public sealed class FilterExpressionCondition : FilterNode
{
    public required ExpressionNode Left { get; init; }

    public required FilterOperator Operator { get; init; }

    public ExpressionNode? Right { get; init; }
}

public sealed class RelativeDateValue
{
    public required string Type { get; init; }

    public required string Unit { get; init; }

    public required int Amount { get; init; }

    public string From { get; init; } = "now";

    /// <summary>
    /// Anchors the shifted instant to a boundary of this unit. Named <c>snapTo</c> since
    /// 2026-08-28; the former <c>truncate</c> is still read.
    /// </summary>
    public string? SnapTo { get; init; }

    /// <summary>
    /// Which edge of <see cref="SnapTo"/> to anchor to (<c>start</c> by default, or <c>end</c> —
    /// the last millisecond before the next period starts).
    /// </summary>
    public string? SnapEdge { get; init; }

    /// <summary>
    /// Signed integer shift applied after <see cref="SnapTo"/> / <see cref="SnapEdge"/>
    /// (query contract 1.4.0+).
    /// </summary>
    public RelativeDateOffset? Offset { get; init; }
}

public sealed class RelativeDateOffset
{
    public required string Unit { get; init; }

    public required int Amount { get; init; }
}

public sealed class FieldRefValue
{
    public required string Type { get; init; }

    public required string Name { get; init; }
}

public sealed class FilterGroup : FilterNode
{
    /// <summary>
    /// Named <c>operator</c> like a condition's, not <c>op</c>: the same idea spelled two ways in
    /// neighbouring nodes of one tree was a needless thing to remember (canonical contract,
    /// 2026-08-28). The old spelling is still accepted on read — see the converter.
    /// </summary>
    public required LogicalOperator Operator { get; init; }

    public IReadOnlyList<FilterNode> Items { get; init; } = [];
}

[JsonConverter(typeof(ExpressionNodeJsonConverter))]
public abstract class ExpressionNode
{
    public required string Type { get; init; }
}

public sealed class ValueExpression : ExpressionNode
{
    public object? Value { get; init; }
}

public sealed class FieldExpression : ExpressionNode
{
    public required string Field { get; init; }
}

public sealed class NowExpression : ExpressionNode
{
    public string Anchor { get; init; } = "now";
}

public sealed class DateDiffExpression : ExpressionNode
{
    public required string Unit { get; init; }

    public required ExpressionNode Start { get; init; }

    public required ExpressionNode End { get; init; }
}

public sealed class AggregateExpression : ExpressionNode
{
    public required AggregateAction Action { get; init; }

    /// <summary>
    /// The column to aggregate. Mutually exclusive with <see cref="Expression"/>; one of the two is
    /// required for every action except <c>count</c> and <c>exists</c>.
    /// </summary>
    public string? Field { get; init; }

    /// <summary>
    /// A per-row expression to aggregate instead of a column — <c>avg(a + b)</c>,
    /// <c>sum(price * qty)</c> (canonical contract, 2026-09-30; additive, <see cref="Field"/> stays).
    /// </summary>
    /// <remarks>
    /// Only <c>value</c>, <c>field</c> and <c>math</c> nodes may appear inside it: it is evaluated
    /// once per row, so a nested aggregate has no group to fold. A row whose operand is null — a null
    /// field, a non-numeric operand, a division by zero — is skipped exactly like a null field value.
    /// Fields referenced here are aggregated, never inferred as grouping keys.
    /// </remarks>
    public ExpressionNode? Expression { get; init; }

    public FilterNode? Filter { get; init; }
}

/// <summary>
/// <c>left op right</c>. A null operand gives null, and so does a division by zero — not an error
/// and not infinity (canonical contract) — in every engine, over rows and over aggregates alike.
/// </summary>
public sealed class MathExpression : ExpressionNode
{
    public required MathOperator Operator { get; init; }

    public required ExpressionNode Left { get; init; }

    public required ExpressionNode Right { get; init; }
}

public enum FilterOperator
{
    [JsonStringEnumMemberName("=")]
    Equal,

    [JsonStringEnumMemberName("!=")]
    NotEqual,

    [JsonStringEnumMemberName("<")]
    LessThan,

    [JsonStringEnumMemberName("<=")]
    LessThanOrEqual,

    [JsonStringEnumMemberName(">")]
    GreaterThan,

    [JsonStringEnumMemberName(">=")]
    GreaterThanOrEqual,

    [JsonStringEnumMemberName("IN")]
    In,

    [JsonStringEnumMemberName("NOT_IN")]
    NotIn,

    [JsonStringEnumMemberName("LIKE")]
    Like,

    [JsonStringEnumMemberName("NOT_LIKE")]
    NotLike,

    [JsonStringEnumMemberName("IS_NULL")]
    IsNull,

    [JsonStringEnumMemberName("IS_NOT_NULL")]
    IsNotNull
}

public enum LogicalOperator
{
    [JsonStringEnumMemberName("AND")]
    And,

    [JsonStringEnumMemberName("OR")]
    Or,

    [JsonStringEnumMemberName("NOT")]
    Not
}

public enum AggregateAction
{
    [JsonStringEnumMemberName("sum")]
    Sum,

    [JsonStringEnumMemberName("avg")]
    Avg,

    [JsonStringEnumMemberName("min")]
    Min,

    [JsonStringEnumMemberName("max")]
    Max,

    [JsonStringEnumMemberName("count")]
    Count,

    [JsonStringEnumMemberName("countDistinct")]
    CountDistinct,

    [JsonStringEnumMemberName("exists")]
    Exists
}

public enum MathOperator
{
    [JsonStringEnumMemberName("+")]
    Add,

    [JsonStringEnumMemberName("-")]
    Subtract,

    [JsonStringEnumMemberName("*")]
    Multiply,

    [JsonStringEnumMemberName("/")]
    Divide
}


/// <summary>
/// Shared JSON reading helpers for the query DSL converters.
/// </summary>
internal static class QueryJsonReader
{
    /// <summary>
    /// Reads the canonical <c>operator</c> property, falling back to the pre-2026-08-28 spelling
    /// <c>op</c>. Configurations written before the rename keep working; nothing emits <c>op</c>
    /// any more.
    /// </summary>
    public static string GetRequiredOperator(JsonElement node, string ownerLabel)
    {
        if (TryReadString(node, "operator", out var canonical))
        {
            return canonical;
        }

        return TryReadString(node, "op", out var legacy)
            ? legacy
            : throw new JsonException($"Property 'operator' is required on a {ownerLabel}.");
    }

    private static bool TryReadString(JsonElement node, string propertyName, out string value)
    {
        if (node.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String)
        {
            var raw = property.GetString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                value = raw;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }
}

internal sealed class FilterNodeJsonConverter : JsonConverter<FilterNode>
{
    public override FilterNode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var type = GetRequiredString(root, "type");

        return type switch
        {
            "condition" => new FilterCondition
            {
                Type = type,
                Field = GetRequiredString(root, "field"),
                Operator = EnumMemberParser.Parse<FilterOperator>(GetRequiredString(root, "operator")),
                Value = root.TryGetProperty("value", out var value) ? JsonValueReader.Read(value) : null
            },
            "group" => new FilterGroup
            {
                Type = type,
                Operator = EnumMemberParser.Parse<LogicalOperator>(QueryJsonReader.GetRequiredOperator(root, "group")),
                Items = ReadFilterItems(root, options)
            },
            "expression" => ReadExpressionCondition(root, type, options),
            _ => throw new JsonException($"Unsupported filter type '{type}'.")
        };
    }

    public override void Write(Utf8JsonWriter writer, FilterNode value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, (object)value, options);
    }

    private static FilterExpressionCondition ReadExpressionCondition(JsonElement root, string type, JsonSerializerOptions options)
    {
        if (!root.TryGetProperty("left", out var leftProp) || leftProp.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Filter expression condition requires object 'left'.");
        }

        var left = JsonSerializer.Deserialize<ExpressionNode>(leftProp.GetRawText(), options)
            ?? throw new JsonException("Invalid 'left' expression.");

        var op = EnumMemberParser.Parse<FilterOperator>(QueryJsonReader.GetRequiredOperator(root, "expression condition"));
        if (op is FilterOperator.In or FilterOperator.NotIn or FilterOperator.Like or FilterOperator.NotLike)
        {
            throw new JsonException($"Filter expression condition does not support operator '{op}'.");
        }

        var isUnary = op is FilterOperator.IsNull or FilterOperator.IsNotNull;
        var hasRight = root.TryGetProperty("right", out var rightProp) && rightProp.ValueKind != JsonValueKind.Undefined;

        if (isUnary)
        {
            if (hasRight)
            {
                throw new JsonException($"Filter expression condition with '{op}' must not specify 'right'.");
            }

            return new FilterExpressionCondition
            {
                Type = type,
                Left = left,
                Operator = op
            };
        }

        if (!hasRight || rightProp.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"Filter expression condition with '{op}' requires object 'right'.");
        }

        var right = JsonSerializer.Deserialize<ExpressionNode>(rightProp.GetRawText(), options)
            ?? throw new JsonException("Invalid 'right' expression.");

        return new FilterExpressionCondition
        {
            Type = type,
            Left = left,
            Operator = op,
            Right = right
        };
    }

    private static IReadOnlyList<FilterNode> ReadFilterItems(JsonElement root, JsonSerializerOptions options)
    {
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Filter group requires array 'items'.");
        }

        return [.. items.EnumerateArray().Select(item => JsonSerializer.Deserialize<FilterNode>(item.GetRawText(), options) ?? throw new JsonException("Invalid filter item."))];
    }

    private static string GetRequiredString(JsonElement node, string propertyName)
    {
        if (!node.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"Property '{propertyName}' is required.");
        }

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? throw new JsonException($"Property '{propertyName}' cannot be empty.") : value;
    }
}

internal sealed class ExpressionNodeJsonConverter : JsonConverter<ExpressionNode>
{
    public override ExpressionNode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var type = GetRequiredString(root, "type");

        return type switch
        {
            "value" => new ValueExpression
            {
                Type = type,
                Value = root.TryGetProperty("value", out var value) ? JsonValueReader.Read(value) : null
            },
            "field" => new FieldExpression
            {
                Type = type,
                Field = GetRequiredString(root, "field")
            },
            "now" => new NowExpression
            {
                Type = type,
                Anchor = root.TryGetProperty("anchor", out var anchor) && anchor.ValueKind == JsonValueKind.String
                    ? anchor.GetString() ?? "now"
                    : "now"
            },
            "dateDiff" => new DateDiffExpression
            {
                Type = type,
                Unit = GetRequiredString(root, "unit"),
                Start = ReadExpression(root, "start", options),
                End = ReadExpression(root, "end", options)
            },
            "aggregate" => ReadAggregate(root, options),
            "math" => new MathExpression
            {
                Type = type,
                Operator = EnumMemberParser.Parse<MathOperator>(QueryJsonReader.GetRequiredOperator(root, "math expression")),
                Left = ReadExpression(root, "left", options),
                Right = ReadExpression(root, "right", options)
            },
            _ => throw new JsonException($"Unsupported expression type '{type}'.")
        };
    }

    public override void Write(Utf8JsonWriter writer, ExpressionNode value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, (object)value, options);
    }


    /// <summary>
    /// Reads an aggregate, translating the two pre-2026-08-28 spellings that no longer exist in
    /// <see cref="AggregateAction"/>.
    /// </summary>
    /// <remarks>
    /// <c>action: "field"</c> was the duplicate spelling of a plain column reference and becomes a
    /// <see cref="FieldExpression"/>; <c>exist</c> and <c>count_distinct</c> were renamed. Reading
    /// them keeps a host that has not yet migrated its stored queries working against a deployed
    /// API — the deploy order then stops mattering.
    /// </remarks>
    private static ExpressionNode ReadAggregate(JsonElement root, JsonSerializerOptions options)
    {
        var action = GetRequiredString(root, "action");
        var field = root.TryGetProperty("field", out var fieldProperty) && fieldProperty.ValueKind != JsonValueKind.Null
            ? fieldProperty.GetString()
            : null;

        // Read explicitly like every other property here: a property this method does not name is
        // silently dropped, and an aggregate over a dropped expression would quietly become a
        // `count` of rows or a "requires 'field'" error that names the wrong problem.
        var expression = root.TryGetProperty("expression", out var expressionProperty) && expressionProperty.ValueKind != JsonValueKind.Null
            ? JsonSerializer.Deserialize<ExpressionNode>(expressionProperty.GetRawText(), options)
                ?? throw new JsonException("Invalid aggregate 'expression'.")
            : null;

        if (string.Equals(action, "field", StringComparison.OrdinalIgnoreCase))
        {
            if (expression is not null)
            {
                throw new JsonException("Aggregate action 'field' does not take an 'expression'.");
            }

            return new FieldExpression
            {
                Type = "field",
                Field = field ?? throw new JsonException("Aggregate action 'field' requires a field.")
            };
        }

        return new AggregateExpression
        {
            Type = "aggregate",
            Action = EnumMemberParser.Parse<AggregateAction>(NormalizeAggregateAction(action)),
            Field = field,
            Expression = expression,
            Filter = root.TryGetProperty("filter", out var filter) && filter.ValueKind != JsonValueKind.Null
                ? JsonSerializer.Deserialize<FilterNode>(filter.GetRawText(), options)
                : null
        };
    }

    private static string NormalizeAggregateAction(string action)
    {
        return action.ToLowerInvariant() switch
        {
            "exist" => "exists",
            "count_distinct" => "countDistinct",
            _ => action
        };
    }

    private static ExpressionNode ReadExpression(JsonElement root, string propertyName, JsonSerializerOptions options)
    {
        if (!root.TryGetProperty(propertyName, out var expression))
        {
            throw new JsonException($"Math expression requires '{propertyName}'.");
        }

        return JsonSerializer.Deserialize<ExpressionNode>(expression.GetRawText(), options)
            ?? throw new JsonException($"Invalid '{propertyName}' expression.");
    }

    private static string GetRequiredString(JsonElement node, string propertyName)
    {
        if (!node.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"Property '{propertyName}' is required.");
        }

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? throw new JsonException($"Property '{propertyName}' cannot be empty.") : value;
    }
}

internal static class JsonValueReader
{
    public static object? Read(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt32(out var intValue) => intValue,
            JsonValueKind.Number when value.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when value.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.Array => value.EnumerateArray().Select(Read).ToArray(),
            JsonValueKind.Object => ReadObject(value),
            _ => throw new JsonException($"Unsupported JSON value kind '{value.ValueKind}'.")
        };
    }

    private static object ReadObject(JsonElement value)
    {
        if (!value.TryGetProperty("type", out var typeProperty) || typeProperty.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("Object values require string property 'type'.");
        }

        var type = typeProperty.GetString();
        return type switch
        {
            "relativeDate" => new RelativeDateValue
            {
                Type = type,
                Unit = GetRequiredString(value, "unit"),
                Amount = GetRequiredInt32(value, "amount"),
                From = value.TryGetProperty("from", out var from) && from.ValueKind == JsonValueKind.String
                    ? from.GetString() ?? "now"
                    : "now",
                SnapTo = ReadSnapTo(value),
                SnapEdge = ReadOptionalString(value, "snapEdge"),
                Offset = ReadRelativeDateOffset(value)
            },
            "field" => new FieldRefValue
            {
                Type = type,
                Name = GetRequiredString(value, "name")
            },
            _ => throw new JsonException($"Unsupported object value type '{type}'.")
        };
    }

    private static RelativeDateOffset? ReadRelativeDateOffset(JsonElement node)
    {
        if (!node.TryGetProperty("offset", out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Property 'offset' must be an object with 'unit' and 'amount'.");
        }

        return new RelativeDateOffset
        {
            Unit = GetRequiredString(property, "unit"),
            Amount = GetRequiredInt32(property, "amount")
        };
    }

    private static string? ReadOptionalString(JsonElement node, string propertyName)
    {
        if (!node.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : throw new JsonException($"Property '{propertyName}' must be a string.");
    }

    private static int? ReadOptionalInt32(JsonElement node, string propertyName)
    {
        if (!node.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.TryGetInt32(out var value)
            ? value
            : throw new JsonException($"Property '{propertyName}' must be an integer.");
    }

    private static string GetRequiredString(JsonElement node, string propertyName)
    {
        if (!node.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"Property '{propertyName}' is required.");
        }

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? throw new JsonException($"Property '{propertyName}' cannot be empty.") : value;
    }

    /// <summary>
    /// Reads <c>snapTo</c>, falling back to the pre-2026-08-28 <c>truncate</c>.
    /// </summary>
    private static string? ReadSnapTo(JsonElement value)
    {
        if (value.TryGetProperty("snapTo", out var snapTo) && snapTo.ValueKind == JsonValueKind.String)
        {
            return snapTo.GetString();
        }

        return value.TryGetProperty("truncate", out var truncate) && truncate.ValueKind == JsonValueKind.String
            ? truncate.GetString()
            : null;
    }

    private static int GetRequiredInt32(JsonElement node, string propertyName)
    {
        if (!node.TryGetProperty(propertyName, out var property) || !property.TryGetInt32(out var value))
        {
            throw new JsonException($"Property '{propertyName}' must be an integer.");
        }

        return value;
    }
}

internal static class EnumMemberParser
{
    public static TEnum Parse<TEnum>(string value)
        where TEnum : struct, Enum
    {
        foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var attribute = field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>();
            if (string.Equals(attribute?.Name, value, StringComparison.OrdinalIgnoreCase)
                || string.Equals(field.Name, value, StringComparison.OrdinalIgnoreCase))
            {
                return (TEnum)field.GetValue(null)!;
            }
        }

        throw new JsonException($"Unsupported {typeof(TEnum).Name} value '{value}'.");
    }
}
