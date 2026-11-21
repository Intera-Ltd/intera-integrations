using Intera.Common.Infrastructure.Http;
using System.Net;
using System.Text.Json;

namespace Intera.ZohoInventory.Api.Infrastructure;

/// <summary>
/// The credential this deployment holds was refused, lacks a scope, or is missing. Surfaces as 401
/// rather than the global filter's 502: Zoho answered, and correctly — what is wrong is this
/// deployment's configuration.
/// </summary>
public sealed class ZohoAuthenticationException(string message) : Exception(message);

/// <summary>Zoho refused the request itself. Surfaces as 400 carrying Zoho's own text.</summary>
public sealed class ZohoRequestException(string message) : Exception(message);

/// <summary>
/// Zoho's call limit was hit — the daily allowance, the per-minute rate or the concurrency cap.
/// Surfaces as 429: nothing is broken, the organization is out of calls for now.
/// </summary>
public sealed class ZohoLimitException(string message) : Exception(message);

/// <summary>
/// The query would read more from Zoho than this deployment allows in one go. Surfaces as 400
/// asking the caller to narrow the filter.
/// </summary>
/// <remarks>
/// Refused rather than truncated on purpose. A capped read drops documents without saying so, and a
/// dashboard total over the remainder looks exactly like a correct one; a refusal is at least
/// visible.
/// </remarks>
public sealed class ZohoQueryTooBroadException(string message) : Exception(message);

/// <summary>
/// Turns a Zoho failure into the exception that carries the right status out of this API.
/// </summary>
/// <remarks>
/// <para>
/// Zoho's error body is <c>{ "code": &lt;int&gt;, "message": "…" }</c>, and the code is more telling
/// than the HTTP status: code 57 ("not authorized to perform this operation") is a missing scope or
/// a role without access to the module, and code 14 is an invalid or expired access token.
/// </para>
/// <para>
/// The three kinds of limit all answer 429, and code 1070 is the concurrency one. Exceeding the
/// per-minute rate is the dangerous kind: Zoho then blocks the organization for a while, which is why
/// <c>ZohoRequestThrottle</c> exists at all.
/// </para>
/// </remarks>
public static class ZohoErrors
{
    /// <summary>Zoho's code for an invalid or expired OAuth access token.</summary>
    public const int InvalidTokenCode = 14;

    public static Exception Translate(HttpApiException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var (code, message) = Read(exception.ResponseBody);
        var detail = message ?? $"HTTP {(int)exception.StatusCode}";

        if (exception.StatusCode == HttpStatusCode.TooManyRequests || code == 1070)
        {
            return new ZohoLimitException($"Zoho Inventory refused the request because a call limit was reached: {detail}");
        }

        return exception.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new ZohoAuthenticationException(
                $"Zoho Inventory rejected the credential: {detail}"),
            HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed => new ZohoRequestException(
                $"Zoho Inventory refused the request: {detail}"),
            _ => exception
        };
    }

    /// <summary>
    /// Whether a failed call was refused for its access token, i.e. worth one retry with a new token.
    /// </summary>
    public static bool IsInvalidToken(HttpApiException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.StatusCode == HttpStatusCode.Unauthorized
            && Read(exception.ResponseBody).Code is null or InvalidTokenCode;
    }

    /// <summary>Reads Zoho's <c>code</c>/<c>message</c> pair; either may be absent.</summary>
    public static (int? Code, string? Message) Read(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return Read(document.RootElement);
        }
        catch (JsonException)
        {
            return (null, body.Trim());
        }
    }

    public static (int? Code, string? Message) Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        int? code = root.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.Number
            && codeElement.TryGetInt32(out var parsed)
                ? parsed
                : null;

        string? message = root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
            ? messageElement.GetString()
            : null;

        return (code, string.IsNullOrWhiteSpace(message) ? null : message);
    }
}
