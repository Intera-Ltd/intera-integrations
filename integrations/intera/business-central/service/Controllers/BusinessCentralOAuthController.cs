using Intera.BusinessCentral.Api.Services;
using Intera.Common.Authentication;
using Intera.Common.Swagger;
using Microsoft.AspNetCore.Mvc;

namespace Intera.BusinessCentral.Api.Controllers;

/// <summary>
/// Diagnostics for the service-to-service OAuth 2.0 client-credentials token cache.
/// </summary>
[ApiController]
[Route("oauth")]
[RequireAccessToken]
[SwaggerManualToken]
public sealed class BusinessCentralOAuthController(IBusinessCentralOAuthService oauthService) : ControllerBase
{
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        await oauthService.RefreshAsync(cancellationToken).ConfigureAwait(false);
        return Ok(new { status = "refreshed" });
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        return Ok(await oauthService.GetStatusAsync(cancellationToken).ConfigureAwait(false));
    }
}
