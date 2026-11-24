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
public sealed class StockTransferLineController(IDocumentQueryService documentService) : UnleashedControllerBase
{
    /// <summary>
    /// Stock moving between sites: one row per transfer line, naming both the source and the
    /// destination warehouse, the quantity moved and the source's stock before the move.
    /// </summary>
    /// <remarks>
    /// The only source here whose upstream resource offers no date filter at all — a window is
    /// narrowed through <c>last_modified_on</c>, and anything else is filtered over the snapshot.
    /// </remarks>
    [HttpPost("query")]
    public Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        return QueryRowsAsync(
            cancellationToken2 => documentService.GetStockTransferLineRowsAsync(query, cancellationToken2),
            query,
            StockTransferLineQueryFields.All,
            cancellationToken);
    }
}
