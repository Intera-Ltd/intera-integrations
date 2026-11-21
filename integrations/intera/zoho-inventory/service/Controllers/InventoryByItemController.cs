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
public sealed class InventoryByItemController(IDocumentService documentService) : ZohoControllerBase
{
    /// <summary>
    /// Stock per item, organization-wide, committed stock included: the item list, then one
    /// <c>/itemdetails</c> call per batch of items. Services that track no inventory are not here.
    /// </summary>
    [HttpPost("query")]
    public Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        return QueryRowsAsync(
            cancellationToken2 => documentService.GetInventoryByItemRowsAsync(query, cancellationToken2),
            query,
            ZohoQueryFields.InventoryByItem,
            cancellationToken);
    }
}
