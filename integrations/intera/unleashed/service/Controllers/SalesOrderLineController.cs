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
public sealed class SalesOrderLineController(IDocumentQueryService documentService) : UnleashedControllerBase
{
    /// <summary>
    /// Committed outgoing demand: one row per sales order line, with the order's customer, warehouse,
    /// status and dates repeated on it.
    /// </summary>
    /// <remarks>
    /// <c>line_type</c> tells a stock line from a freight or handling charge — Unleashed writes
    /// <c>"Charge"</c> for the latter and leaves it null otherwise. Charges are kept rather than
    /// filtered out, so the source still agrees with the order as the customer sees it; a demand
    /// question excludes them with <c>line_type IS_NULL</c>.
    /// </remarks>
    [HttpPost("query")]
    public Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        return QueryRowsAsync(
            cancellationToken2 => documentService.GetSalesOrderLineRowsAsync(query, cancellationToken2),
            query,
            SalesOrderLineQueryFields.All,
            cancellationToken);
    }
}
