using Intera.Common.Authentication;
using Intera.Common.Controllers;
using Intera.Common.Swagger;
using Intera.ZohoInventory.Api.Infrastructure;
using Intera.ZohoInventory.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Intera.ZohoInventory.Api.Controllers;

/// <summary>
/// Diagnostics only. The refresh token is minted once by hand in the Zoho API console, so there is no
/// login or callback to expose — only the state of the access token traded for it.
/// </summary>
/// <remarks>
/// <c>GET /oauth/status</c> reports the accounts host and the API root the token came with, which is
/// the fastest way to see a data-center mismatch: an organization on <c>.eu</c> read through
/// <c>.com</c> fails with nothing more telling than <c>invalid_client</c>.
/// </remarks>
[ApiController]
[Route("oauth")]
public sealed class ZohoOAuthController(IZohoOAuthService oauthService) : ControllerBase
{
    [HttpPost("refresh")]
    [RequireAccessToken]
    [SwaggerManualToken]
    public async Task<IActionResult> RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await oauthService.RefreshAsync(cancellationToken).ConfigureAwait(false);
            return Ok(ApiResponse<object>.Success(new { status = "refreshed" }));
        }
        catch (ZohoAuthenticationException exception)
        {
            return Unauthorized(ApiResponse<object?>.Fail(exception.Message));
        }
    }

    [HttpGet("status")]
    public IActionResult Status()
    {
        return Ok(oauthService.GetStatus());
    }
}
