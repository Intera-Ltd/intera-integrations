using Intera.Common.Authentication;
using Intera.Common.Controllers;
using Intera.Common.Models.Query;
using Intera.Common.Swagger;
using Intera.Xero.Api.Models;
using Intera.Xero.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Intera.Xero.Api.Controllers;

/// <summary>
/// Every data source behind one route, <c>POST /api/Source/{source}/query</c>, rather than a controller each: the
/// sources differ only in their field list and loader, both of which live in <see cref="XeroSources"/> and
/// <see cref="XeroDataService"/>. <c>GET /api/Schema/sources</c> lists them.
/// </summary>
[RequireAccessToken]
[SwaggerManualToken]
public sealed class SourceController(IXeroDataService dataService) : XeroControllerBase
{
    [HttpPost("{source}/query")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryAsync(
        string source,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        if (!XeroSources.ById.TryGetValue(source, out var definition))
        {
            return NotFound(ApiResponse<object?>.Fail(
                $"Unknown data source '{source}'. Known: {string.Join(", ", XeroSources.All.Select(static item => item.Id))}."));
        }

        var tenantId = RequestedTenantId;

        return await QueryRowsAsync(
            ct => dataService.LoadAsync(definition, query, tenantId, ct),
            query,
            definition.Allowlist,
            cancellationToken).ConfigureAwait(false);
    }
}
