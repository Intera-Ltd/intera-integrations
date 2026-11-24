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
public sealed class WarehouseController(ICatalogService catalogService) : UnleashedControllerBase
{
    /// <summary>
    /// The warehouse register: code, name, address and whether the site is the default or obsolete.
    /// This is the master list every stock and movement source here links back to through
    /// <c>warehouse_code</c>, and the list a stock-on-hand query fans out over — so it is read once
    /// and cached rather than re-fetched per request.
    /// </summary>
    /// <remarks>
    /// Obsolete warehouses are included, and <c>is_obsolete</c> is an ordinary column: a closed site
    /// still holds the history of what moved through it, so which of them to show is the caller's
    /// decision rather than one made here.
    /// </remarks>
    [HttpPost("query")]
    public Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        return QueryRowsAsync(catalogService.GetWarehouseRowsAsync, query, WarehouseQueryFields.All, cancellationToken);
    }
}
