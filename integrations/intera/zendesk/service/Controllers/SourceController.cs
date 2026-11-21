using Intera.Common.Authentication;
using Intera.Common.Controllers;
using Intera.Common.Models.Query;
using Intera.Common.Swagger;
using Intera.Zendesk.Api.Models;
using Intera.Zendesk.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Intera.Zendesk.Api.Controllers;

/// <summary>
/// Every data source behind one route, <c>POST /api/Source/{source}/query</c>, rather than a controller each: the
/// sources differ only in their field list and loader, both of which live in <see cref="ZendeskSources"/> and
/// <see cref="ZendeskDataService"/>. <c>GET /api/Schema/sources</c> lists them.
/// </summary>
[RequireAccessToken]
[SwaggerManualToken]
public sealed class SourceController(IZendeskDataService dataService) : ZendeskControllerBase
{
    [HttpPost("{source}/query")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryAsync(
        string source,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        if (!ZendeskSources.ById.TryGetValue(source, out var definition))
        {
            return NotFound(ApiResponse<object?>.Fail(
                $"Unknown data source '{source}'. Known: {string.Join(", ", ZendeskSources.All.Select(static item => item.Id))}."));
        }

        return await QueryRowsAsync(
            ct => dataService.LoadAsync(definition, query, ct),
            query,
            definition.Allowlist,
            cancellationToken).ConfigureAwait(false);
    }
}
