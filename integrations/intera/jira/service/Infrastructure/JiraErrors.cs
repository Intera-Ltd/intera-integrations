namespace Intera.Jira.Api.Infrastructure;

/// <summary>
/// This deployment holds no usable grant: nobody has authorised it yet, the refresh token expired or was revoked,
/// the site was removed from the grant, or a scope or a Jira permission is missing. Surfaces as <b>401</b> rather
/// than the global filter's 502 — Atlassian answered, and the thing to fix is this deployment's connection.
/// </summary>
public sealed class JiraAuthorizationException(string message) : Exception(message);

/// <summary>
/// The request cannot be answered as asked: Jira refused it (invalid JQL, an unknown id), the site is ambiguous,
/// or the query would read more than this deployment allows. Surfaces as <b>400</b>; rows are never truncated
/// instead, because a dashboard cannot tell a truncated count from a correct one.
/// </summary>
public sealed class JiraRequestException(string message) : Exception(message);

/// <summary>
/// Jira's rate limit, past what is worth waiting out inside one request — a <c>Retry-After</c> longer than
/// <c>Jira:MaxRetryAfterSeconds</c>, or still limited after the retries. Surfaces as <b>429</b>.
/// </summary>
public sealed class JiraRateLimitException(string message, TimeSpan? retryAfter) : Exception(message)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>Jira failed on its side (5xx, an unreadable answer). Surfaces as <b>502</b>.</summary>
public sealed class JiraUpstreamException(string message) : Exception(message);
