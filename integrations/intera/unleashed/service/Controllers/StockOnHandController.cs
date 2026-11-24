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
public sealed class StockOnHandController(IStockOnHandService stockOnHandService) : UnleashedControllerBase
{
    /// <summary>
    /// Current stock at SKU × warehouse grain: quantity on hand, allocated, available, on purchase,
    /// and the average and total cost behind them.
    /// </summary>
    /// <remarks>
    /// Every row is read under an explicit warehouse code, so the grain is guaranteed rather than
    /// assumed — which costs one upstream call per warehouse. A filter naming warehouses is applied
    /// before the fan-out, including one written against <c>warehouse_name</c>, so a query about one
    /// site costs one call.
    /// </remarks>
    [HttpPost("query")]
    public Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        return QueryRowsAsync(
            cancellationToken2 => stockOnHandService.GetRowsAsync(query, cancellationToken2),
            query,
            StockOnHandQueryFields.All,
            cancellationToken);
    }
}
