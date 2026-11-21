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
public sealed class ShipmentController(IDocumentService documentService) : ZohoControllerBase
{
    /// <summary>
    /// One row per shipment. Zoho has no shipment list, so shipments are read out of the packages they
    /// were created for: each package is one Zoho call, and packages the list marks unshipped are
    /// skipped.
    /// </summary>
    [HttpPost("query")]
    public Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        return QueryRowsAsync(
            cancellationToken2 => documentService.GetShipmentRowsAsync(query, cancellationToken2),
            query,
            ZohoQueryFields.Shipments,
            cancellationToken);
    }
}
