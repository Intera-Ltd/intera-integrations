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
public sealed class InventoryByLocationController(IDocumentService documentService) : ZohoControllerBase
{
    /// <summary>
    /// Stock per item and location: the item list, then one <c>/itemdetails</c> call per batch of
    /// items. Without multi-warehouse, each item is one row at the organization's only location.
    /// </summary>
    [HttpPost("query")]
    public Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        return QueryRowsAsync(
            cancellationToken2 => documentService.GetInventoryByLocationRowsAsync(query, cancellationToken2),
            query,
            ZohoQueryFields.InventoryByLocation,
            cancellationToken);
    }
}
