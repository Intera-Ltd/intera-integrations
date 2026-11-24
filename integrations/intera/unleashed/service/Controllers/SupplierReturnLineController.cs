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
public sealed class SupplierReturnLineController(IDocumentQueryService documentService) : UnleashedControllerBase
{
    /// <summary>
    /// Stock going back to suppliers: one row per supplier return line, with the return's supplier,
    /// warehouse, status and originating purchase order.
    /// </summary>
    /// <remarks>
    /// <c>stock_on_hand_before_return</c> and the landed price on either side of the return are
    /// Unleashed's own snapshot of the moment, so a return can be valued without joining back to the
    /// stock source at a date it no longer holds.
    /// </remarks>
    [HttpPost("query")]
    public Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        return QueryRowsAsync(
            cancellationToken2 => documentService.GetSupplierReturnLineRowsAsync(query, cancellationToken2),
            query,
            SupplierReturnLineQueryFields.All,
            cancellationToken);
    }
}
