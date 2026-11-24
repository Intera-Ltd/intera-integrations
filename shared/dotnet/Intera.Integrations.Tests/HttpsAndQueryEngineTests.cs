using Intera.Common.Infrastructure.Http;
using Intera.Common.Infrastructure.OAuth;
using Intera.Common.Infrastructure.Query;
using Intera.Common.Models.Query;
using Xunit;

namespace Intera.Integrations.Tests;

public sealed class HttpsAndQueryEngineTests
{
    [Theory]
    [InlineData("https://api.example.com")]
    [InlineData("https://api.example.com:8443/v1/path")]
    [InlineData("http://127.0.0.1:8080/callback")]
    [InlineData("http://localhost:8080/oauth/token")]
    [InlineData("http://[::1]:5000/api")]
    public void HttpsUriValidator_AcceptsHttpsAndLoopbackHttp(string url)
    {
        var parsed = HttpsUriValidator.RequireHttpsOrLoopback(url, "Test:Endpoint");
        Assert.True(parsed.IsAbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/relative/path")]
    [InlineData("http://api.example.com")]
    [InlineData("http://10.0.0.5:8080/oauth/token")]
    [InlineData("https://user:pass@example.com/api")]
    [InlineData("https://api.example.com/callback#frag")]
    [InlineData("ftp://api.example.com/file")]
    public void HttpsUriValidator_RejectsInsecureOrMalformedUrls(string url)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => HttpsUriValidator.RequireHttpsOrLoopback(url, "Test:Endpoint"));
        Assert.Contains("Test:Endpoint", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileOAuthRedirectUriStore_PersistsAtomicallyWithoutTrailingTempFile()
    {
        var ct = TestContext.Current.CancellationToken;
        var tempDir = Path.Combine(Path.GetTempPath(), $"intera-redir-{Guid.NewGuid():N}");
        var filePath = Path.Combine(tempDir, "redirect-uri.json");

        try
        {
            var writer = new FileOAuthRedirectUriStore(() => filePath);
            Assert.Null(await writer.GetAsync(ct));

            const string callbackUrl = "https://core.example.com/api/v1/webhooks/public-123";
            await writer.SaveAsync(callbackUrl, ct);

            Assert.Equal(callbackUrl, await writer.GetAsync(ct));
            Assert.True(File.Exists(filePath));
            Assert.False(File.Exists($"{filePath}.tmp"));

            var reader = new FileOAuthRedirectUriStore(() => filePath);
            Assert.Equal(callbackUrl, await reader.GetAsync(ct));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void InMemoryQueryEngine_CapsUnboundedQueriesToDefaultMaxRowsAndPreservesFilteredCount()
    {
        Assert.Equal(10_000, InMemoryQueryEngine.DefaultMaxRows);

        var fields = new Dictionary<string, InMemoryQueryField>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = new("id", row => row["id"]),
            ["amount"] = new("amount", row => row["amount"]),
            ["threshold"] = new("threshold", row => row["threshold"])
        };

        var source = Enumerable.Range(1, 10_500)
            .Select(i => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
            {
                ["id"] = i,
                ["amount"] = i * 2,
                ["threshold"] = i
            })
            .ToList();

        var engine = new InMemoryQueryEngine(fields);
        var unbounded = engine.Execute(source, query: null);
        Assert.Equal(InMemoryQueryEngine.DefaultMaxRows, unbounded.Count);
        Assert.Equal(10_500, engine.FilteredRowCount);
        Assert.Equal(1, unbounded[0]["id"]);
        Assert.Equal(10_000, unbounded[^1]["id"]);

        var skipped = engine.Execute(source, new QueryRequest { Skip = 200 });
        Assert.Equal(InMemoryQueryEngine.DefaultMaxRows, skipped.Count);
        Assert.Equal(10_500, engine.FilteredRowCount);
        Assert.Equal(201, skipped[0]["id"]);
        Assert.Equal(10_200, skipped[^1]["id"]);

        var tail = engine.Execute(source, new QueryRequest { Skip = 10_200 });
        Assert.Equal(300, tail.Count);
        Assert.Equal(10_500, engine.FilteredRowCount);

        var fieldRefFiltered = engine.Execute(
            source.Take(10),
            new QueryRequest
            {
                Filter = new FilterCondition
                {
                    Type = "condition",
                    Field = "amount",
                    Operator = FilterOperator.GreaterThan,
                    Value = new FieldRefValue { Type = "field", Name = "threshold" }
                }
            });
        Assert.Equal(10, fieldRefFiltered.Count);
    }

    [Fact]
    public void CalendarDayAndWholePeriodRanges_EvaluateAsHalfOpenIntervalsAcrossInMemoryAndPushdown()
    {
        var fixedNowUtc = new DateTimeOffset(2026, 3, 18, 14, 35, 20, TimeSpan.Zero);
        var utc = TimeZoneInfo.Utc;

        var today = new RelativeDateValue
        {
            Type = "relativeDate",
            From = "today",
            Unit = "day",
            Amount = 0
        };
        var todayRange = RelativeDateResolver.ResolveRange(today, fixedNowUtc, utc);
        Assert.Equal(new DateTime(2026, 3, 18, 0, 0, 0), todayRange.Start);
        Assert.Equal(new DateTime(2026, 3, 19, 0, 0, 0), todayRange.EndExclusive);

        var thisMonth = new RelativeDateValue
        {
            Type = "relativeDate",
            From = "today",
            Unit = "month",
            Amount = 0,
            SnapTo = "month",
            SnapEdge = "whole"
        };
        var monthRange = RelativeDateResolver.ResolveRange(thisMonth, fixedNowUtc, utc);
        Assert.Equal(new DateTime(2026, 3, 1, 0, 0, 0), monthRange.Start);
        Assert.Equal(new DateTime(2026, 4, 1, 0, 0, 0), monthRange.EndExclusive);

        var pushdown = QueryPushdownExtractor.Extract(
            new QueryRequest
            {
                TimeZone = "UTC",
                Filter = new FilterCondition
                {
                    Type = "condition",
                    Field = "transaction_date",
                    Operator = FilterOperator.Equal,
                    Value = thisMonth
                }
            },
            dateField: "transaction_date",
            keyFields: [],
            nowUtc: fixedNowUtc);
        Assert.Equal(new DateOnly(2026, 3, 1), pushdown.From);
        Assert.Equal(new DateOnly(2026, 3, 31), pushdown.To);

        var nowToday = DateTimeOffset.UtcNow.Date;
        var rows = new Dictionary<string, object?>[]
        {
            new() { ["id"] = 1, ["transaction_date"] = nowToday.AddDays(-1).AddHours(23) },
            new() { ["id"] = 2, ["transaction_date"] = nowToday.AddHours(14).AddMinutes(23) },
            new() { ["id"] = 3, ["transaction_date"] = nowToday.AddDays(1) }
        };
        var memFields = new Dictionary<string, InMemoryQueryField>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = new("id", r => r["id"]),
            ["transaction_date"] = new("transaction_date", r => r["transaction_date"])
        };
        var matchedToday = new InMemoryQueryEngine(memFields).Execute(
            rows,
            new QueryRequest
            {
                TimeZone = "UTC",
                Filter = new FilterCondition
                {
                    Type = "condition",
                    Field = "transaction_date",
                    Operator = FilterOperator.Equal,
                    Value = today
                }
            });
        Assert.Equal(2, Assert.Single(matchedToday)["id"]);
    }
}
