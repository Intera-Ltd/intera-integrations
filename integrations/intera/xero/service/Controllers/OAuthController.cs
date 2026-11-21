using Intera.Common.Authentication;
using Intera.Common.Controllers;
using Intera.Common.Swagger;
using Intera.Xero.Api.Infrastructure;
using Intera.Xero.Api.Services;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace Intera.Xero.Api.Controllers;

/// <summary>
/// Connecting this deployment to Xero: <c>GET /oauth/authorize-url</c> (authenticated) hands out a single-use link to
/// Xero's consent page, Xero redirects back to <c>GET /oauth/callback</c>, and the grant is stored and refreshed from
/// then on. <c>GET /oauth/status</c> is unauthenticated so health/status tooling can read it.
/// </summary>
/// <remarks>
/// There is no unauthenticated <c>/oauth/login</c>: whoever starts the flow decides which
/// Xero organisation the deployment reads, so starting it takes this API's access token — see
/// <see cref="XeroTokenService"/>.
/// </remarks>
[ApiController]
[Route("oauth")]
public sealed class OAuthController(XeroTokenService tokens) : ControllerBase
{
    /// <summary>A link to open in a browser, valid once and for 15 minutes.</summary>
    [HttpGet("authorize-url")]
    [RequireAccessToken]
    [SwaggerManualToken]
    public ActionResult<ApiResponse<object>> GetAuthorizeUrl()
    {
        try
        {
            return Ok(ApiResponse<object>.Success(new { url = tokens.BuildAuthorizationUrl() }));
        }
        catch (Exception exception) when (XeroControllerBase.Map(this, exception) is { } result)
        {
            return result;
        }
    }

    /// <summary>Where Xero sends the browser back; answers a small HTML page, since a person is looking at it.</summary>
    [HttpGet("callback")]
    public async Task<IActionResult> CallbackAsync(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            return Page(HttpStatusCode.BadRequest, "Xero did not connect", $"Xero answered: {error} {errorDescription}");
        }

        try
        {
            await tokens.ExchangeAuthorizationCodeAsync(code ?? string.Empty, state, cancellationToken).ConfigureAwait(false);
            return Page(HttpStatusCode.OK, "Xero connected", "The grant is stored and will be refreshed automatically. This window can be closed.");
        }
        catch (Exception exception) when (exception is XeroRequestException or XeroAuthorizationException or XeroUpstreamException or InvalidOperationException or HttpRequestException)
        {
            return Page(HttpStatusCode.BadRequest, "Xero did not connect", exception.Message);
        }
    }

    [HttpPost("refresh")]
    [RequireAccessToken]
    [SwaggerManualToken]
    public async Task<ActionResult<ApiResponse<object>>> RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await tokens.RefreshAsync(cancellationToken).ConfigureAwait(false);
            return Ok(ApiResponse<object>.Success(new { status = "refreshed" }));
        }
        catch (Exception exception) when (XeroControllerBase.Map(this, exception) is { } result)
        {
            return result;
        }
    }

    [HttpGet("status")]
    public async Task<ActionResult<XeroOAuthStatus>> StatusAsync(CancellationToken cancellationToken) =>
        Ok(await tokens.GetStatusAsync(cancellationToken).ConfigureAwait(false));

    private ContentResult Page(HttpStatusCode status, string title, string message) => new()
    {
        StatusCode = (int)status,
        ContentType = "text/html; charset=utf-8",
        Content = $"<!doctype html><html><head><meta charset=\"utf-8\"><title>{WebUtility.HtmlEncode(title)}</title></head>"
            + $"<body style=\"font-family:sans-serif;margin:3em\"><h1>{WebUtility.HtmlEncode(title)}</h1><p>{WebUtility.HtmlEncode(message)}</p></body></html>"
    };
}
