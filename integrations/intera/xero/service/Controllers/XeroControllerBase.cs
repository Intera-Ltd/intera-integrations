using Intera.Common.Controllers;
using Intera.Common.Infrastructure.OAuth;
using Intera.Common.Infrastructure.Query;
using Intera.Common.Models.Query;
using Intera.Xero.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace Intera.Xero.Api.Controllers;

/// <summary>
/// Shared shape of every data endpoint here: read from Xero, then run the query DSL over the rows in memory.
/// </summary>
/// <remarks>
/// <para>
/// No usable grant is a <b>401</b> — this deployment's Xero connection, not the caller's token. A request Xero refuses,
/// an ambiguous organisation and a query too broad to run are a <b>400</b>; Xero's rate limit past what is worth
/// waiting out is a <b>429</b>; Xero unreachable or failing is a <b>502</b>.
/// </para>
/// <para>
/// <b>The query is run once over no rows before anything is read from Xero</b>, so an unknown field or a malformed
/// <c>relativeDate</c> is refused without spending Xero's call allowance. The <see cref="ArgumentException"/> catch is
/// also what turns a bad <c>relativeDate</c> thrown by <c>QueryPushdownExtractor</c> into a 400 rather than a 500 —
/// see the root CLAUDE.md's sharp edges.
/// </para>
/// </remarks>
public abstract class XeroControllerBase : ApiControllerBase
{
    /// <summary>
    /// The organisation to read, from the integration instance. Optional: without it the deployment's
    /// <c>Xero:TenantId</c>, or the grant's only organisation, is read.
    /// </summary>
    public const string TenantHeaderName = "X-Xero-Tenant-Id";

    protected string? RequestedTenantId =>
        Request.Headers.TryGetValue(TenantHeaderName, out var value) && !string.IsNullOrWhiteSpace(value) ? value.ToString() : null;

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
            case XeroAuthorizationException:
                return controller.Unauthorized(ApiResponse<object?>.Fail(exception.Message));

            case XeroRequestException or ArgumentException:
                return controller.BadRequest(ApiResponse<object?>.Fail(exception.Message));

            case OAuthRedirectUriConflictException:
                return controller.Conflict(ApiResponse<object?>.Fail(exception.Message));

            case XeroRateLimitException rateLimit:
                if (rateLimit.RetryAfter is { } wait)
                {
                    controller.Response.Headers.RetryAfter = Math.Ceiling(wait.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                return controller.StatusCode(StatusCodes.Status429TooManyRequests, ApiResponse<object?>.Fail(exception.Message));

            case XeroUpstreamException:
                return controller.StatusCode(StatusCodes.Status502BadGateway, ApiResponse<object?>.Fail(exception.Message));

            // Xero did not answer at all. Without this the global filter answers a bare 500 that names nothing — and
            // the inner exception (refused, reset, DNS) is the part worth reading, so it goes to the log.
            case HttpRequestException or TaskCanceledException { InnerException: TimeoutException }:
                controller.HttpContext.RequestServices.GetRequiredService<ILogger<XeroControllerBase>>()
                    .LogWarning(exception, "Xero could not be reached for {Path}.", controller.Request.Path);
                return controller.StatusCode(
                    StatusCodes.Status502BadGateway,
                    ApiResponse<object?>.Fail($"Xero could not be reached: {exception.Message}"));

            // Configuration missing on this side (no client id, no token file path): not the caller's fault, nor Xero's.
            case InvalidOperationException:
                return controller.StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<object?>.Fail(exception.Message));

            default:
                return null;
        }
    }
}
