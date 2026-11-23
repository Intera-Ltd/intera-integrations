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
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        await oauthService.RefreshAsync(cancellationToken).ConfigureAwait(false);
        return Ok(new { status = "refreshed" });
    }

    [HttpGet("status")]
    public IActionResult Status()
    {
        return Ok(oauthService.GetStatus());
    }
}
