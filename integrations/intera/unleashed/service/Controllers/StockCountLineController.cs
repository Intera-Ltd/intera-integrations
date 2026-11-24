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
public sealed class StockCountLineController(IDocumentQueryService documentService) : UnleashedControllerBase
{
    /// <summary>
    /// Physical against system: one row per stock count line, carrying the system quantity
    /// (<c>on_hand_quantity</c>) beside the counted one (<c>count_quantity</c>).
    /// </summary>
    /// <remarks>
    /// The variance is deliberately not a column. <c>count_quantity - on_hand_quantity</c> in the
    /// query is already correct, including for a line nobody has counted yet: Unleashed leaves the
    /// count null there, and a null variance is the honest answer for a line that has not been walked
    /// past. A column defaulting it to zero would report the whole shelf as missing.
    /// </remarks>
    [HttpPost("query")]
    public Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        return QueryRowsAsync(
            cancellationToken2 => documentService.GetStockCountLineRowsAsync(query, cancellationToken2),
            query,
            StockCountLineQueryFields.All,
            cancellationToken);
    }
}
