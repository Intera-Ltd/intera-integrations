using Intera.Common.Authentication;
using Intera.Common.Controllers;
using Intera.Common.Models.Query;
using Intera.Common.Swagger;
using Intera.ZohoInventory.Api.Models.Query;
using Intera.ZohoInventory.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Intera.ZohoInventory.Api.Controllers;

[RequireAccessToken]
[SwaggerManualToken]
public sealed class SalespersonController(ICatalogService catalogService) : ZohoControllerBase
{
    /// <summary>
    /// Salespersons, active and inactive, from one call to Zoho's undocumented
    /// <c>/salespersons</c>.
    /// </summary>
    [HttpPost("query")]
    public Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        return QueryRowsAsync(catalogService.GetSalespersonRowsAsync, query, ZohoQueryFields.Salespersons, cancellationToken);
    }
}
