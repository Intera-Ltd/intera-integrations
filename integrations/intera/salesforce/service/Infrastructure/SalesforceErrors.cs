using System.Net;
using System.Text.Json;
using Intera.Common.Infrastructure.Http;
using Intera.Salesforce.Api.Models;

namespace Intera.Salesforce.Api.Infrastructure;

/// <summary>
/// Salesforce rejected the sign-in. Surfaces as <b>401</b>: Salesforce is reachable and answered
/// correctly, and what is wrong is this deployment's own configuration.
/// </summary>
public sealed class SalesforceAuthenticationException(string message) : Exception(message);

/// <summary>
/// Salesforce refused the query itself. Surfaces as <b>400</b>, carrying Salesforce's own text.
/// </summary>
/// <remarks>
/// The most common causes are worth naming, because each is a real answer rather than a fault:
/// <c>INVALID_TYPE</c> means the object is not available in this org (Orders, for one, is a feature
/// that can be off), <c>INVALID_FIELD</c> means the run-as user cannot see that field, and
/// <c>MALFORMED_QUERY</c> means this project built SOQL the org would not parse. Left to the global
/// filter all three would be an opaque 502 with the query echoed back inside it.
/// </remarks>
public sealed class SalesforceQueryException(string message) : Exception(message);

/// <summary>
/// The org is out of API calls for the day. Surfaces as <b>429</b>.
/// </summary>
/// <remarks>
/// Its own type because it is the one upstream failure that is neither the caller's fault nor a bug:
/// every Salesforce edition has a rolling 24-hour API request allowance, and a Developer Edition org
/// gets 15,000. A dashboard refreshing eight widgets on a timer can reach that.
/// </remarks>
public sealed class SalesforceLimitException(string message) : Exception(message);

/// <summary>
/// Reads Salesforce's error bodies, which come in two unrelated shapes.
/// </summary>
public static class SalesforceErrors
{
    /// <summary>
    /// The error code Salesforce returns when an access token is no longer usable — expired, or
    /// revoked before it expired. It is the signal to re-authenticate and retry once.
    /// </summary>
    public const string InvalidSessionCode = "INVALID_SESSION_ID";

    private const string RequestLimitCode = "REQUEST_LIMIT_EXCEEDED";

    /// <summary>
    /// Turns an upstream failure into the named exception that carries the right status, or returns
    /// <see langword="null"/> to leave it to the global filter's 502.
    /// </summary>
    public static Exception? Translate(HttpApiException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var (code, message) = Read(exception.ResponseBody);
        var described = string.IsNullOrWhiteSpace(message)
            ? $"Salesforce answered {(int)exception.StatusCode}."
            : message;

        if (string.Equals(code, RequestLimitCode, StringComparison.Ordinal))
        {
            return new SalesforceLimitException($"Salesforce API request limit reached: {described}");
        }

        if (string.Equals(code, InvalidSessionCode, StringComparison.Ordinal))
        {
            // Never translated into a 401 for the caller: the session belongs to this service, not to
            // them, and the client re-authenticates and retries instead. Reaching here means the
            // retry itself failed.
            return new SalesforceAuthenticationException($"Salesforce rejected this service's session twice: {described}");
        }

        return exception.StatusCode switch
        {
            HttpStatusCode.BadRequest or HttpStatusCode.NotFound => new SalesforceQueryException($"Salesforce refused the query: {described}"),
            HttpStatusCode.Unauthorized => new SalesforceAuthenticationException($"Salesforce rejected the sign-in: {described}"),
            HttpStatusCode.Forbidden => new SalesforceQueryException($"Salesforce refused access: {described}"),
            _ => null
        };
    }

    /// <summary>Whether a failed call should be retried after re-authenticating.</summary>
    public static bool IsExpiredSession(HttpApiException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception.StatusCode != HttpStatusCode.Unauthorized)
        {
            return false;
        }

        var (code, _) = Read(exception.ResponseBody);

        // The code is checked rather than the status alone, but a 401 with no readable body is
        // treated as an expired session too: the retry costs one token request, and refusing to make
        // it would leave the service permanently broken after an org's session timeout elapsed.
        return string.IsNullOrEmpty(code) || string.Equals(code, InvalidSessionCode, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads the error code and message out of a response body.
    /// </summary>
    /// <remarks>
    /// Two shapes, from two different parts of Salesforce. The REST API answers with an array —
    /// <c>[{"message":"…","errorCode":"MALFORMED_QUERY"}]</c> — while the OAuth token endpoint uses
    /// the OAuth object form, <c>{"error":"invalid_client","error_description":"…"}</c>. Both are
    /// read here so a caller of either gets the sentence Salesforce actually wrote.
    /// </remarks>
    public static (string? Code, string? Message) Read(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var element = root.ValueKind switch
            {
                JsonValueKind.Array when root.GetArrayLength() > 0 => root[0],
                JsonValueKind.Object => root,
                _ => (JsonElement?)null
            };

            if (element is not { } error)
            {
                return (null, Trim(body));
            }

            var dto = error.Deserialize<SalesforceErrorDto>(JsonSerializerOptions.Web);
            if (dto is null)
            {
                return (null, Trim(body));
            }

            var message = FirstNonEmpty(dto.Message, dto.ErrorDescription, dto.Error);
            return (FirstNonEmpty(dto.ErrorCode, dto.Error), message ?? Trim(body));
        }
        catch (JsonException)
        {
            // A non-JSON body is usually Salesforce's HTML login page, which means the request went
            // somewhere other than the API. Passing the raw text on is more useful than swallowing it.
            return (null, Trim(body));
        }
    }

    private static string? FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(static candidate => !string.IsNullOrWhiteSpace(candidate));

    private static string Trim(string body)
    {
        var trimmed = body.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500];
    }
}
