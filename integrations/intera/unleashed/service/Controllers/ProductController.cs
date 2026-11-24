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
public sealed class ProductController(ICatalogService catalogService) : UnleashedControllerBase
{
    /// <summary>
    /// SKU master data: code, description, barcode, grouping, unit of measure, dimensions, the stock
    /// alert levels and the standing costs and prices. <c>product_code</c> is the key every movement
    /// source here joins on.
    /// </summary>
    /// <remarks>
    /// The per-customer sell price and the ten sell-price tiers are not exposed: the first needs a
    /// customer named in the upstream request, and the second is ten columns that are empty in most
    /// accounts. Neither is the Xero account mapping, which describes the accounting integration
    /// rather than the product.
    /// </remarks>
    [HttpPost("query")]
    public Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        return QueryRowsAsync(
            cancellationToken2 => catalogService.GetProductRowsAsync(query, cancellationToken2),
            query,
            ProductQueryFields.All,
            cancellationToken);
    }
}
