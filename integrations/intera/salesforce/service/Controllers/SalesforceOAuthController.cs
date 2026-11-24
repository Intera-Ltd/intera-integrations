using Intera.Common.Authentication;
using Intera.Common.Controllers;
using Intera.Common.Swagger;
using Intera.Salesforce.Api.Infrastructure;
using Intera.Salesforce.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Intera.Salesforce.Api.Controllers;

/// <summary>
/// Diagnostics only: this is a client-credentials grant, so there
/// is no user-facing login or callback to expose here.
/// </summary>
/// <remarks>
/// Salesforce sends no <c>expires_in</c>: the expiry reported by <c>GET /oauth/status</c> is this
/// service's own configured TTL rather than anything Salesforce said, and seeing that is the fastest
/// way to tell a stale cached session apart from a wrong client secret. It also reports the
/// <c>instance_url</c> the token came with, which is the host every query then goes to.
/// </remarks>
[ApiController]
[Route("oauth")]
[RequireAccessToken]
[SwaggerManualToken]
public sealed class SalesforceOAuthController(ISalesforceOAuthService oauthService) : ControllerBase
{
    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await oauthService.RefreshAsync(cancellationToken).ConfigureAwait(false);
            return Ok(ApiResponse<object>.Success(new { status = "refreshed" }));
        }
        catch (SalesforceAuthenticationException exception)
        {
            return Unauthorized(ApiResponse<object?>.Fail(exception.Message));
        }
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        return Ok(await oauthService.GetStatusAsync(cancellationToken).ConfigureAwait(false));
    }
}
