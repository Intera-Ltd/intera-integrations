using Intera.Common.Infrastructure.Http;
using System.Net;
using System.Text.Json;

namespace Intera.Unleashed.Api.Infrastructure;

/// <summary>
/// The credential this deployment holds was refused, or is missing. Surfaces as 401 rather than the
/// global filter's 502: Unleashed answered, and correctly — what is wrong is this deployment's
/// configuration.
/// </summary>
public sealed class UnleashedAuthenticationException(string message) : Exception(message);

/// <summary>
/// Unleashed refused the request itself (a bad parameter, a resource the plan does not include).
/// Surfaces as 400 carrying Unleashed's own text.
/// </summary>
public sealed class UnleashedRequestException(string message) : Exception(message);

/// <summary>
/// The account is out of API calls. Surfaces as 429: nothing is broken, the budget is simply spent.
/// </summary>
public sealed class UnleashedLimitException(string message) : Exception(message);

/// <summary>
/// Turns an upstream HTTP failure into the exception that carries the right status out of this API.
/// </summary>
/// <remarks>
/// <para>
/// Unleashed answers a wrong signature, a wrong API id and an account whose plan has no API access
/// with the same <b>403</b>, and its body is the only thing that separates them — which is why the
/// body is read rather than the status alone. A 403 is mapped to 401 here on purpose: from the
/// caller's side the fact worth reporting is "this deployment's Unleashed credential is not
/// working", and 403 would suggest the caller's own token was the problem.
/// </para>
/// <para>
/// The call-limit response is the one shape that is not documented anywhere. Both spellings are
/// therefore accepted: a plain 429, and a 403 whose body says the limit was reached. A 403 that says
/// nothing about a limit stays an authentication failure, which is what it usually is.
/// </para>
/// </remarks>
public static class UnleashedErrors
{
    public static Exception Translate(HttpApiException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var detail = Describe(exception.ResponseBody);

        if (exception.StatusCode == HttpStatusCode.TooManyRequests || LooksLikeCallLimit(exception.ResponseBody))
        {
            return new UnleashedLimitException(
                $"Unleashed refused the request because the account's API call allowance is exhausted: {detail}");
        }

        return exception.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new UnleashedAuthenticationException(
                $"Unleashed rejected the credential: {detail}"),
            HttpStatusCode.BadRequest or HttpStatusCode.NotFound => new UnleashedRequestException(
                $"Unleashed refused the request: {detail}"),
            _ => exception
        };
    }

    /// <summary>
    /// Whether a response body reads as an exhausted call allowance. Deliberately a text match: the
    /// monthly allowance is enforced per subscription plan and Unleashed documents neither the status
    /// nor the payload it answers with, so there is no field to read.
    /// </summary>
    private static bool LooksLikeCallLimit(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return false;
        }

        return (responseBody.Contains("limit", StringComparison.OrdinalIgnoreCase)
                || responseBody.Contains("quota", StringComparison.OrdinalIgnoreCase))
            && (responseBody.Contains("exceed", StringComparison.OrdinalIgnoreCase)
                || responseBody.Contains("reached", StringComparison.OrdinalIgnoreCase)
                || responseBody.Contains("usage", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The most informative sentence available from an error body. Unleashed answers with a JSON
    /// object carrying <c>Description</c> (and sometimes <c>Errors</c>) on a refused request, but a
    /// rejected signature comes back as plain text, so both are handled and the raw body is the
    /// fallback rather than an invented message.
    /// </summary>
    private static string Describe(string responseBody)
    {
        var trimmed = responseBody.Trim();

        if (trimmed.Length == 0)
        {
            return "no reason given";
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.String)
            {
                return root.GetString() ?? trimmed;
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in (string[])["Description", "description", "Message", "message", "Error", "error"])
                {
                    if (root.TryGetProperty(name, out var property)
                        && property.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(property.GetString()))
                    {
                        return property.GetString()!;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON — the raw body is still the most informative thing there is.
        }

        return trimmed;
    }
}
