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
public sealed class PurchaseOrderLineController(IDocumentQueryService documentService) : UnleashedControllerBase
{
    /// <summary>
    /// Incoming stock: one row per purchase order line, carrying the order it belongs to (number,
    /// date, status, supplier, warehouse) alongside the quantity ordered, the quantity receipted and
    /// what is still outstanding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>outstanding_quantity</c> is computed here rather than left to the caller. Writing it as
    /// <c>order_quantity - receipt_quantity</c> in the query reads null on every line that has not
    /// been receipted at all — which is exactly the set of lines the question is about — because
    /// Unleashed sends no receipt quantity until there is one.
    /// </para>
    /// <para>
    /// The order's own <c>SubTotal</c>/<c>TaxTotal</c>/<c>Total</c> are not repeated on the line: at
    /// this grain a sum over them would multiply each order by its line count. The line amounts add up
    /// correctly, and the number of orders is <c>countDistinct(order_number)</c>.
    /// </para>
    /// </remarks>
    [HttpPost("query")]
    public Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        return QueryRowsAsync(
            cancellationToken2 => documentService.GetPurchaseOrderLineRowsAsync(query, cancellationToken2),
            query,
            PurchaseOrderLineQueryFields.All,
            cancellationToken);
    }
}
