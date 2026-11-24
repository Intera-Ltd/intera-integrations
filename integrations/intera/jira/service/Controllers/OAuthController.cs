using Intera.Common.Authentication;
using Intera.Common.Controllers;
using Intera.Common.Infrastructure.OAuth;
using Intera.Common.Swagger;
using Intera.Jira.Api.Infrastructure;
using Intera.Jira.Api.Services;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text.Json.Serialization;

namespace Intera.Jira.Api.Controllers;

/// <summary>
/// Connecting this deployment to Jira: <c>GET /oauth/authorize-url</c> (authenticated) hands out a single-use link to
/// Atlassian's consent page, Atlassian redirects back to <c>GET /oauth/callback</c> — through INTERA's webhook relay,
/// whose address the plugin registers with <c>PUT /oauth/redirect-uri</c> — and the grant is stored and refreshed from
/// then on. <c>GET /oauth/status</c> is unauthenticated so connection checks can read it.
/// </summary>
/// <remarks>
/// There is no unauthenticated <c>/oauth/login</c>: whoever starts the flow decides which
/// Jira site, as which user, the deployment reads, so starting it takes this API's access token — see
/// <see cref="JiraTokenService"/>.
/// </remarks>
[ApiController]
[Route("oauth")]
public sealed class OAuthController(JiraTokenService tokens) : ControllerBase
{
    /// <summary>A link to open in a browser, valid once and for 15 minutes.</summary>
    [HttpGet("authorize-url")]
    [RequireAccessToken]
    [SwaggerManualToken]
    public async Task<ActionResult<ApiResponse<object>>> GetAuthorizeUrlAsync(CancellationToken cancellationToken)
    {
        try
        {
            var url = await tokens.BuildAuthorizationUrlAsync(cancellationToken).ConfigureAwait(false);
            return Ok(ApiResponse<object>.Success(new { url }));
        }
        catch (Exception exception) when (JiraControllerBase.Map(this, exception) is { } result)
        {
            return result;
        }
    }

    /// <summary>
    /// INTERA's callback address for this deployment — the public webhook URL Core gives the plugin's <c>oauth-callback</c>
    /// handler, sent by <c>registerWebhook</c> whenever the instance is saved. 409 when <c>Jira:RedirectUri</c> differs.
    /// </summary>
    [HttpPut("redirect-uri")]
    [RequireAccessToken]
    [SwaggerManualToken]
    public async Task<ActionResult<ApiResponse<OAuthRedirectUriState>>> RegisterRedirectUriAsync(
        [FromBody] RedirectUriRegistration? registration,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await tokens.RegisterRedirectUriAsync(registration?.RedirectUri, cancellationToken).ConfigureAwait(false);
            return Ok(ApiResponse<OAuthRedirectUriState>.Success(state));
        }
        catch (Exception exception) when (JiraControllerBase.Map(this, exception) is { } result)
        {
            return result;
        }
    }

    /// <summary>Where Atlassian sends the browser back; answers a small HTML page, since a person is looking at it.</summary>
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
            return Page(HttpStatusCode.BadRequest, "Jira did not connect", $"Atlassian answered: {error} {errorDescription}");
        }

        try
        {
            await tokens.ExchangeAuthorizationCodeAsync(code ?? string.Empty, state, cancellationToken).ConfigureAwait(false);
            return Page(HttpStatusCode.OK, "Jira connected", "The grant is stored and will be refreshed automatically. This window can be closed.");
        }
        catch (Exception exception) when (exception is JiraRequestException or JiraAuthorizationException or JiraUpstreamException or InvalidOperationException or HttpRequestException or TaskCanceledException { InnerException: TimeoutException })
        {
            return Page(HttpStatusCode.BadRequest, "Jira did not connect", exception.Message);
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
        catch (Exception exception) when (JiraControllerBase.Map(this, exception) is { } result)
        {
            return result;
        }
    }

    [HttpGet("status")]
    public async Task<ActionResult<JiraOAuthStatus>> StatusAsync(CancellationToken cancellationToken) =>
        Ok(await tokens.GetStatusAsync(cancellationToken).ConfigureAwait(false));

    public sealed record RedirectUriRegistration([property: JsonPropertyName("redirect_uri")] string? RedirectUri);

    private ContentResult Page(HttpStatusCode status, string title, string message) => new()
    {
        StatusCode = (int)status,
        ContentType = "text/html; charset=utf-8",
        Content = $"<!doctype html><html><head><meta charset=\"utf-8\"><title>{WebUtility.HtmlEncode(title)}</title></head>"
            + $"<body style=\"font-family:sans-serif;margin:3em\"><h1>{WebUtility.HtmlEncode(title)}</h1><p>{WebUtility.HtmlEncode(message)}</p></body></html>"
    };
}
