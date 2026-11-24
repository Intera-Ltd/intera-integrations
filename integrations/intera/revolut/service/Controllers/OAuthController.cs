using Intera.Common.Authentication;
using Intera.Common.Controllers;
using Intera.Common.Infrastructure.Http;
using Intera.Common.Infrastructure.OAuth;
using Intera.Common.Swagger;
using Intera.Revolut.Api.Infrastructure;
using Intera.Revolut.Api.Models;
using Intera.Revolut.Api.Services;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Intera.Revolut.Api.Controllers;

[ApiController]
[Route("oauth")]
[RequireAccessToken]
[SwaggerManualToken]
public sealed class OAuthController(
    IRevolutOAuthService oauthService,
    ILogger<OAuthController> logger) : ControllerBase
{
    [HttpPut("redirect-uri")]
    public async Task<ActionResult<ApiResponse<OAuthRedirectUriState>>> RegisterRedirectUriAsync(
        [FromBody] RedirectUriRegistration? registration,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await oauthService
                .RegisterRedirectUriAsync(registration?.RedirectUri, cancellationToken)
                .ConfigureAwait(false);
            return Ok(ApiResponse<OAuthRedirectUriState>.Success(state));
        }
        catch (Exception exception) when (MapFailure(this, exception) is { } result)
        {
            return result;
        }
    }

    [HttpGet("authorize-url")]
    public async Task<ActionResult<ApiResponse<RevolutAuthorizeUrlDto>>> GetAuthorizeUrlAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var data = await oauthService.BuildAuthorizeUrlAsync(cancellationToken).ConfigureAwait(false);
            return Ok(ApiResponse<RevolutAuthorizeUrlDto>.Success(data));
        }
        catch (Exception exception) when (MapFailure(this, exception) is { } result)
        {
            return result;
        }
    }

    [HttpGet("callback")]
    public async Task<IActionResult> CallbackAsync(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            logger.LogWarning("Revolut OAuth callback returned upstream error code {OAuthError}.", error);
            return Page(
                HttpStatusCode.BadRequest,
                "Revolut Business did not connect",
                "Authorization was declined or failed at Revolut. Generate a new sign-in link in INTERA and try again.");
        }

        try
        {
            await oauthService.ExchangeAuthorizationCodeAsync(code ?? string.Empty, state, cancellationToken)
                .ConfigureAwait(false);
            return Page(
                HttpStatusCode.OK,
                "Revolut Business connected",
                "The OAuth grant is stored and will be refreshed automatically. This window can be closed.");
        }
        catch (RevolutStateException exception)
        {
            logger.LogWarning("Rejected Revolut OAuth callback: {Reason}", exception.Message);
            return Page(
                HttpStatusCode.BadRequest,
                "Revolut Business did not connect",
                "The authorization link is missing, invalid, or expired. Generate a new sign-in link in INTERA (valid once for 15 minutes) and try again.");
        }
        catch (Exception exception) when (exception is HttpApiException or HttpRequestException or InvalidOperationException or CryptographicException or FileNotFoundException or TaskCanceledException { InnerException: TimeoutException })
        {
            logger.LogError(exception, "Revolut OAuth token exchange failed.");
            return Page(
                HttpStatusCode.BadGateway,
                "Revolut Business did not connect",
                "Token exchange with Revolut failed. Verify the container settings and service logs, then try again.");
        }
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<ApiResponse<object>>> RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await oauthService.RefreshAsync(cancellationToken).ConfigureAwait(false);
            return Ok(ApiResponse<object>.Success(new { status = "refreshed" }));
        }
        catch (Exception exception) when (MapFailure(this, exception) is { } result)
        {
            return result;
        }
    }

    [HttpGet("status")]
    public async Task<ActionResult<RevolutOAuthStatusDto>> StatusAsync(CancellationToken cancellationToken) =>
        Ok(await oauthService.GetStatusAsync(cancellationToken).ConfigureAwait(false));

    internal static ObjectResult? MapFailure(ControllerBase controller, Exception exception) =>
        exception switch
        {
            RevolutAuthorizationException => controller.Unauthorized(ApiResponse<object?>.Fail(exception.Message)),
            RevolutStateException or ArgumentException => controller.BadRequest(ApiResponse<object?>.Fail(exception.Message)),
            OAuthRedirectUriConflictException => controller.Conflict(ApiResponse<object?>.Fail(exception.Message)),
            InvalidOperationException => controller.StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiResponse<object?>.Fail(exception.Message)),
            _ => null
        };

    public sealed record RedirectUriRegistration(
        [property: JsonPropertyName("redirect_uri")] string? RedirectUri);

    private static ContentResult Page(HttpStatusCode status, string title, string message) => new()
    {
        StatusCode = (int)status,
        ContentType = "text/html; charset=utf-8",
        Content =
            $"<!doctype html><html><head><meta charset=\"utf-8\"><title>{WebUtility.HtmlEncode(title)}</title></head>"
            + $"<body style=\"font-family:sans-serif;margin:3em\"><h1>{WebUtility.HtmlEncode(title)}</h1><p>{WebUtility.HtmlEncode(message)}</p></body></html>"
    };
}
