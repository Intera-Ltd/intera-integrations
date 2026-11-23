using Intera.Common.Controllers;
using Intera.Common.Infrastructure.OAuth;
using Intera.Common.Infrastructure.Query;
using Intera.Common.Models.Query;
using Intera.Jira.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace Intera.Jira.Api.Controllers;

/// <summary>
/// Shared shape of every data endpoint here: read from Jira, then run the query DSL over the rows in memory.
/// </summary>
/// <remarks>
/// <para>
/// No usable grant, or a scope or permission Jira says is missing, is a <b>401</b> — this deployment's connection, not
/// the caller's token. A request Jira refuses (bad JQL in a scope), an ambiguous site and a query too broad to run are
/// a <b>400</b>; Jira's rate limit past what is worth waiting out is a <b>429</b>; Jira unreachable or failing is a
/// <b>502</b>.
/// </para>
/// <para>
/// <b>The query is run once over no rows before anything is read from Jira</b>, so an unknown field or a malformed
/// <c>relativeDate</c> is refused without spending Jira's quota. The <see cref="ArgumentException"/> catch is also what
/// turns a bad <c>relativeDate</c> thrown by <c>QueryPushdownExtractor</c> into a 400 rather than a 500 — see the root
/// CLAUDE.md's sharp edges.
/// </para>
/// </remarks>
public abstract class JiraControllerBase : ApiControllerBase
{
    /// <summary>
    /// The site to read, from the integration instance: its URL or cloud id. Optional — without it the deployment's
    /// <c>Jira:SiteUrl</c>, or the grant's only site, is read.
    /// </summary>
    public const string SiteHeaderName = "X-Jira-Site";

    /// <summary>
    /// The instance's own JQL scope, URI-encoded (<c>encodeURIComponent</c>): a JQL text may carry characters a header
    /// cannot (a project name in Cyrillic). ANDed with <c>Jira:JqlScope</c>, so it can only narrow.
    /// </summary>
    public const string JqlScopeHeaderName = "X-Jira-Jql-Scope";

    protected string? RequestedSite => Header(SiteHeaderName);

    protected string? RequestedJqlScope => Header(JqlScopeHeaderName) is { } encoded ? Uri.UnescapeDataString(encoded) : null;

    protected async Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryRowsAsync(
        Func<CancellationToken, Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>>> load,
        QueryRequest? query,
        IReadOnlyDictionary<string, InMemoryQueryField> fields,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(load);

        try
        {
            new InMemoryQueryEngine(fields).Execute([], query);

            var rows = await load(cancellationToken).ConfigureAwait(false);

            return QueryInMemoryResponse(rows, query, fields);
        }
        catch (Exception exception) when (MapFailure(exception) is { } result)
        {
            return result;
        }
    }

    /// <summary>The same mapping for the non-query endpoints.</summary>
    protected ObjectResult? MapFailure(Exception exception) => Map(this, exception);

    /// <summary>
    /// The mapping itself, for <see cref="OAuthController"/> too, which cannot inherit this class: its route is
    /// <c>oauth</c>, and <see cref="ApiControllerBase"/>'s <c>api/[controller]</c> would be inherited alongside it.
    /// </summary>
    internal static ObjectResult? Map(ControllerBase controller, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(controller);

        switch (exception)
        {
            case JiraAuthorizationException:
                return controller.Unauthorized(ApiResponse<object?>.Fail(exception.Message));

            case JiraRequestException or ArgumentException:
                return controller.BadRequest(ApiResponse<object?>.Fail(exception.Message));

            case OAuthRedirectUriConflictException:
                return controller.Conflict(ApiResponse<object?>.Fail(exception.Message));

            case JiraRateLimitException rateLimit:
                if (rateLimit.RetryAfter is { } wait)
                {
                    controller.Response.Headers.RetryAfter = Math.Ceiling(wait.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                return controller.StatusCode(StatusCodes.Status429TooManyRequests, ApiResponse<object?>.Fail(exception.Message));

            case JiraUpstreamException:
                return controller.StatusCode(StatusCodes.Status502BadGateway, ApiResponse<object?>.Fail(exception.Message));

            // Jira did not answer at all. Without this the global filter answers a bare 500 that names nothing — and
            // the inner exception (refused, reset, DNS) is the part worth reading, so it goes to the log.
            case HttpRequestException or TaskCanceledException { InnerException: TimeoutException }:
                controller.HttpContext.RequestServices.GetRequiredService<ILogger<JiraControllerBase>>()
                    .LogWarning(exception, "Jira could not be reached for {Path}.", controller.Request.Path);
                return controller.StatusCode(
                    StatusCodes.Status502BadGateway,
                    ApiResponse<object?>.Fail($"Jira could not be reached: {exception.Message}"));

            // Configuration missing on this side (no client id, no token file path): not the caller's fault, nor Jira's.
            case InvalidOperationException:
                return controller.StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<object?>.Fail(exception.Message));

            default:
                return null;
        }
    }

    private string? Header(string name) =>
        Request.Headers.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.ToString().Trim() : null;
}
