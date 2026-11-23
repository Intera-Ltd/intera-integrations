namespace Intera.Zendesk.Api.Infrastructure;

/// <summary>
/// Zendesk refused the credential or the role/plan: a revoked or expired OAuth grant, a missing scope, a user
/// without the right role, or a feature (SLA policies, satisfaction ratings) the plan does not include. Surfaces as
/// <b>401</b> rather than the global filter's 502 — Zendesk answered, and the thing to fix is this deployment's connection.
/// </summary>
public sealed class ZendeskAuthorizationException(string message) : Exception(message);

/// <summary>
/// The request cannot be answered as asked: Zendesk refused it, or the read would be larger than this deployment
/// allows. Surfaces as <b>400</b>; rows are never truncated instead, because a dashboard cannot tell a truncated count
/// from a correct one.
/// </summary>
public class ZendeskRequestException(string message) : Exception(message);

/// <summary>
/// INTERA registered a redirect URL while the deployment sets its own (<c>Zendesk:RedirectUri</c>), and the two differ.
/// Surfaces as <b>409</b>: neither side is wrong on its own, and the deployment's value wins until it is removed.
/// </summary>
public sealed class ZendeskRedirectUriConflictException(string message) : Exception(message);

/// <summary>Zendesk's rate limit, past what is worth waiting out inside one request. Surfaces as <b>429</b>.</summary>
public sealed class ZendeskRateLimitException(string message, TimeSpan? retryAfter) : Exception(message)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>Zendesk failed on its side (5xx, an unreadable answer). Surfaces as <b>502</b>.</summary>
public sealed class ZendeskUpstreamException(string message) : Exception(message);
