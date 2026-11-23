using Intera.Common.Authentication;
using Intera.Common.Controllers;
using Intera.Common.Swagger;
using Intera.ZohoInventory.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Intera.ZohoInventory.Api.Controllers;

[RequireAccessToken]
[SwaggerManualToken]
public sealed class OrganizationController(ISchemaService schemaService) : ZohoControllerBase
{
    /// <summary>
    /// The organization this deployment reads. One Zoho call that proves the refresh token, the data
    /// center, the organization id and the settings scope at once — which is why <c>deploy.sh</c> and
    /// the plugin's "Test connection" both use it.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<ZohoOrganizationDto>>> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            return OkResponse(await schemaService.GetOrganizationAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (MapFailure(exception) is { } result)
        {
            return result;
        }
    }
}
