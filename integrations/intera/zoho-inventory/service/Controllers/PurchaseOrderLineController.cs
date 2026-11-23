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
public sealed class PurchaseOrderLineController(IDocumentService documentService) : ZohoControllerBase
{
    /// <summary>
    /// One row per purchase order line, carrying <c>salesorder_item_id</c> — the sales order line a
    /// drop-shipped or back-ordered purchase was raised for. Each order is one Zoho call.
    /// </summary>
    [HttpPost("query")]
    public Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        return QueryRowsAsync(
            cancellationToken2 => documentService.GetPurchaseOrderLineRowsAsync(query, cancellationToken2),
            query,
            ZohoQueryFields.PurchaseOrderLines,
            cancellationToken);
    }
}
