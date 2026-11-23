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
public sealed class SalesOrderLineController(IDocumentService documentService) : ZohoControllerBase
{
    /// <summary>
    /// One row per sales order line, with the order's customer, dates and status repeated on it. Each
    /// order is one Zoho call; conditions on the order's own fields narrow which orders are read.
    /// </summary>
    [HttpPost("query")]
    public Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        return QueryRowsAsync(
            cancellationToken2 => documentService.GetSalesOrderLineRowsAsync(query, cancellationToken2),
            query,
            ZohoQueryFields.SalesOrderLines,
            cancellationToken);
    }
}
