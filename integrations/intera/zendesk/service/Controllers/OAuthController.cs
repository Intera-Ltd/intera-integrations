using Intera.Common.Authentication;
using Intera.Common.Controllers;
using Intera.Common.Swagger;
using Intera.Zendesk.Api.Infrastructure;
using Intera.Zendesk.Api.Services;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text.Json.Serialization;

namespace Intera.Zendesk.Api.Controllers;

/// <summary>
/// Connecting this deployment to Zendesk: <c>GET /oauth/authorize-url</c> (authenticated) hands out a single-use link to
/// Zendesk's consent page, Zendesk redirects back to <c>GET /oauth/callback</c> — through INTERA's webhook relay, whose
/// address the plugin registers with <c>PUT /oauth/redirect-uri</c> — and the grant is stored and refreshed from then
/// on. <c>GET /oauth/status</c> is unauthenticated so the plugin's
/// connection checks can read it.
/// </summary>
/// <remarks>
/// Like Xero and Jira, there is no unauthenticated <c>/oauth/login</c>: whoever starts
/// the flow decides as which Zendesk user the deployment reads, so starting it takes this API's access token — see
/// <see cref="ZendeskTokenService"/>.
/// </remarks>
[ApiController]
[Route("oauth")]
public sealed class OAuthController(ZendeskTokenService tokens) : ControllerBase
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
        catch (Exception exception) when (ZendeskControllerBase.Map(this, exception) is { } result)
        {
            return result;
        }
    }

    /// <summary>
    /// INTERA's callback address for this deployment — the public webhook URL Core gives the plugin's <c>oauth-callback</c>
    /// handler, sent by the plugin's <c>registerWebhook</c> whenever the instance is saved. 409 when the deployment sets
    /// its own <c>Zendesk:RedirectUri</c> and it differs.
    /// </summary>
    [HttpPut("redirect-uri")]
    [RequireAccessToken]
    [SwaggerManualToken]
    public async Task<ActionResult<ApiResponse<ZendeskRedirectUriState>>> RegisterRedirectUriAsync(
        [FromBody] RedirectUriRegistration? registration,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await tokens.RegisterRedirectUriAsync(registration?.RedirectUri, cancellationToken).ConfigureAwait(false);
            return Ok(ApiResponse<ZendeskRedirectUriState>.Success(state));
        }
        catch (Exception exception) when (ZendeskControllerBase.Map(this, exception) is { } result)
        {
            return result;
        }
    }

    /// <summary>Where Zendesk sends the browser back; answers a small HTML page, since a person is looking at it.</summary>
    /// <remarks>
    /// Unauthenticated, as Zendesk's redirect cannot carry this API's token: the single-use <c>state</c> is the check. From
    /// 4.0 the request usually arrives relayed by the plugin's <c>onWebhook</c> rather than from the browser itself, and
    /// this page travels back the same way.
    /// </remarks>
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
            return Page(HttpStatusCode.BadRequest, "Zendesk did not connect", $"Zendesk answered: {error} {errorDescription}");
        }

        try
        {
            await tokens.ExchangeAuthorizationCodeAsync(code ?? string.Empty, state, cancellationToken).ConfigureAwait(false);
            return Page(HttpStatusCode.OK, "Zendesk connected", "The grant is stored and will be refreshed automatically. This window can be closed.");
        }
        catch (Exception exception) when (exception is ZendeskRequestException or ZendeskAuthorizationException or ZendeskUpstreamException or InvalidOperationException or HttpRequestException or TaskCanceledException { InnerException: TimeoutException })
        {
            return Page(HttpStatusCode.BadRequest, "Zendesk did not connect", exception.Message);
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
        catch (Exception exception) when (ZendeskControllerBase.Map(this, exception) is { } result)
        {
            return result;
        }
    }

    [HttpGet("status")]
    public async Task<ActionResult<ZendeskOAuthStatus>> StatusAsync(CancellationToken cancellationToken) =>
        Ok(await tokens.GetStatusAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Body of <c>PUT /oauth/redirect-uri</c>. The name is spelled out because the snake_case naming policy applies on
    /// read as well — see the root CLAUDE.md's sharp edges.
    /// </summary>
    public sealed record RedirectUriRegistration([property: JsonPropertyName("redirect_uri")] string? RedirectUri);

    private ContentResult Page(HttpStatusCode status, string title, string message) => new()
    {
        StatusCode = (int)status,
        ContentType = "text/html; charset=utf-8",
        Content = $"<!doctype html><html><head><meta charset=\"utf-8\"><title>{WebUtility.HtmlEncode(title)}</title></head>"
            + $"<body style=\"font-family:sans-serif;margin:3em\"><h1>{WebUtility.HtmlEncode(title)}</h1><p>{WebUtility.HtmlEncode(message)}</p></body></html>"
    };
}
