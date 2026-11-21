using Intera.Common.Authentication;
using Intera.Common.Controllers;
using Intera.Common.Swagger;
using Intera.ZohoInventory.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Intera.ZohoInventory.Api.Controllers;

[RequireAccessToken]
[SwaggerManualToken]
public sealed class SchemaController(ISchemaService schemaService) : ZohoControllerBase
{
    /// <summary>
    /// The custom-field columns each source carries in this organization, read from the
    /// organization's custom-field settings — what the plugin's <c>provisionInstance</c> adds to the
    /// static field lists. One Zoho call.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<ZohoSchemaDto>>> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            return OkResponse(await schemaService.GetSchemaAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (MapFailure(exception) is { } result)
        {
            return result;
        }
    }
}
