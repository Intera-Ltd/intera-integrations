namespace Intera.Xero.Api.Infrastructure;

/// <summary>
/// This deployment holds no usable grant: nobody has authorised it yet, the refresh token expired or was revoked,
/// the organisation was disconnected, or a scope is missing. Surfaces as <b>401</b> rather than the global filter's
/// 502 — Xero answered, and the thing to fix is this deployment's connection, usually by authorising it again.
/// </summary>
public sealed class XeroAuthorizationException(string message) : Exception(message);

/// <summary>
/// The request cannot be answered as asked: Xero refused it (a validation error, an unknown id), the organisation
/// is ambiguous, or the query would need more calls than this deployment allows. Surfaces as <b>400</b>; rows are
/// never truncated instead, because a dashboard cannot tell a truncated sum from a correct one.
/// </summary>
public sealed class XeroRequestException(string message) : Exception(message);

/// <summary>
/// Xero's rate limit, past what is worth waiting out inside one request — the daily allowance, or a minute limit
/// asking for a longer wait than <c>Xero:MaxRetryAfterSeconds</c>. Surfaces as <b>429</b>.
/// </summary>
public sealed class XeroRateLimitException(string message, TimeSpan? retryAfter) : Exception(message)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>Xero failed on its side (5xx, an unreadable answer). Surfaces as <b>502</b>.</summary>
public sealed class XeroUpstreamException(string message) : Exception(message);
