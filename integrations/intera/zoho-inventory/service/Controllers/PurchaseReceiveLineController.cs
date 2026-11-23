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
public sealed class PurchaseReceiveLineController(IDocumentService documentService) : ZohoControllerBase
{
    /// <summary>
    /// One row per received line. Each receive is one Zoho call; a date range and a vendor are sent to
    /// Zoho's own list filter first.
    /// </summary>
    [HttpPost("query")]
    public Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        return QueryRowsAsync(
            cancellationToken2 => documentService.GetPurchaseReceiveLineRowsAsync(query, cancellationToken2),
            query,
            ZohoQueryFields.PurchaseReceiveLines,
            cancellationToken);
    }
}
