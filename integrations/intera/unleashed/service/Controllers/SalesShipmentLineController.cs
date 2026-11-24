using Intera.Common.Authentication;
using Intera.Common.Controllers;
using Intera.Common.Models.Query;
using Intera.Common.Swagger;
using Intera.Unleashed.Api.Models.Query;
using Intera.Unleashed.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Intera.Unleashed.Api.Controllers;

[RequireAccessToken]
[SwaggerManualToken]
public sealed class SalesShipmentLineController(IDocumentQueryService documentService) : UnleashedControllerBase
{
    /// <summary>
    /// Actual dispatches: one row per shipment line, with the dispatch date, the tracking details and
    /// the originating sales order.
    /// </summary>
    /// <remarks>
    /// <c>unit_cost_at_time_of_dispatch</c> is the historical figure and <c>unit_cost</c> the current
    /// one; margin work wants the first. Note that the upstream date filter narrows on when a shipment
    /// was <b>created</b>, not on <c>dispatch_date</c>, so a query written against the dispatch date
    /// filters locally over the snapshot.
    /// </remarks>
    [HttpPost("query")]
    public Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        return QueryRowsAsync(
            cancellationToken2 => documentService.GetSalesShipmentLineRowsAsync(query, cancellationToken2),
            query,
            SalesShipmentLineQueryFields.All,
            cancellationToken);
    }
}
