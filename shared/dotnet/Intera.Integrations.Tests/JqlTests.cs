using Intera.Jira.Api.Infrastructure;
using Xunit;

namespace Intera.Integrations.Tests;

public sealed class JqlTests
{
    [Theory]
    [InlineData("project = DEV ORDER BY created DESC")]
    [InlineData("project in (DEV, OPS) order   by priority")]
    [InlineData("project = DEV) OR (project = SECRET")]
    [InlineData("(project = DEV")]
    [InlineData("summary ~ \"unclosed quote")]
    [InlineData("summary ~ 'unclosed single quote")]
    public void ValidateScope_RejectsOrderByInjectionAndUnbalancedDelimiters(string scope)
    {
        Assert.Throws<JiraRequestException>(() => Jql.ValidateScope(scope, "X-Jira-Jql-Scope"));
    }

    [Theory]
    [InlineData("project in (DEV, OPS) AND statusCategory != Done")]
    [InlineData("(project = DEV OR project = OPS) AND labels not in (archived)")]
    [InlineData("summary ~ \"order by should stay literal inside quotes\" AND project = \"A)B\"")]
    public void ValidateScope_AcceptsValidBalancedScopes(string scope)
    {
        var validated = Jql.ValidateScope(scope, "Jira:JqlScope");
        Assert.Equal(scope, validated);
    }

    [Fact]
    public void Build_WrapsValidatedScopesInParenthesesAndJoinsWithAnd()
    {
        var scope = Jql.ValidateScope("project = DEV OR project = OPS", "Jira:JqlScope")!;
        var jql = Jql.Build(new JqlIssueQuery(
            Scopes: [scope],
            Projects: ["DEV"],
            IssueKeys: ["DEV-42"],
            IssueIds: [],
            Dates: [new JqlDateBound("updated", new DateOnly(2026, 11, 10), null)]));

        Assert.Equal(
            "(project = DEV OR project = OPS) AND project in (\"DEV\") AND key in (\"DEV-42\") AND updated >= \"2026-11-09\"",
            jql);
    }
}
