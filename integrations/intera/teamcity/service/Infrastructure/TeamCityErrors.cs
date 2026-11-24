using Intera.Common.Infrastructure.Http;
using System.Net;
using System.Text.Json;

namespace Intera.TeamCity.Api.Infrastructure;

/// <summary>
/// The token this deployment holds was refused, or it cannot see what was asked for. Surfaces as 401
/// rather than the global filter's 502: TeamCity answered, and correctly — what is wrong is this
/// deployment's configuration.
/// </summary>
public sealed class TeamCityAuthenticationException(string message) : Exception(message);

/// <summary>TeamCity refused the request itself (a bad locator, an unknown project). Surfaces as 400.</summary>
public sealed class TeamCityRequestException(string message) : Exception(message);

/// <summary>A query that would fan out over more build configurations than allowed. Surfaces as 400.</summary>
public sealed class TeamCityQueryTooBroadException(string message) : Exception(message);

/// <summary>
/// Turns an upstream HTTP failure into the exception that carries the right status out of this API.
/// </summary>
/// <remarks>
/// A 403 is reported as 401 on purpose, as Unleashed does: from the caller's side the fact worth
/// reporting is "this deployment's TeamCity token is not allowed to do this", and a 403 would suggest
/// the caller's own access token was the problem. TeamCity answers a 403 when the token's user has no
/// role in the project — the usual mistake when setting up a read-only user.
/// </remarks>
public static class TeamCityErrors
{
    public static Exception Translate(HttpApiException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var detail = Describe(exception.ResponseBody);

        return exception.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new TeamCityAuthenticationException(
                $"TeamCity rejected the token (TeamCity:Token): {detail}"),
            HttpStatusCode.Forbidden => new TeamCityAuthenticationException(
                $"TeamCity refused access - the token's user lacks a role in this project: {detail}"),
            HttpStatusCode.BadRequest or HttpStatusCode.NotFound => new TeamCityRequestException(
                $"TeamCity refused the request: {detail}"),
            _ => exception
        };
    }

    /// <summary>
    /// The most informative sentence available. Measured on TeamCity 2026.2, an error body with
    /// <c>Accept: application/json</c> is <c>{"errors":[{"message":…}]}</c>; older servers answer in plain
    /// text, so the raw body is the fallback rather than an invented message.
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

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array)
            {
                var messages = errors.EnumerateArray()
                    .Select(static error => error.ValueKind == JsonValueKind.Object
                        && error.TryGetProperty("message", out var message)
                        && message.ValueKind == JsonValueKind.String
                            ? message.GetString()
                            : null)
                    .Where(static message => !string.IsNullOrWhiteSpace(message))
                    .ToArray();

                if (messages.Length > 0)
                {
                    return string.Join("; ", messages);
                }
            }
        }
        catch (JsonException)
        {
            // Plain text — the raw body is still the most informative thing there is.
        }

        return trimmed.Length > 1000 ? trimmed[..1000] : trimmed;
    }
}
