using Intera.Common.Authentication;
using Intera.Common.Controllers;
using Intera.Common.Swagger;
using Intera.Xero.Api.Clients;
using Intera.Xero.Api.Models;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Intera.Xero.Api.Controllers;

/// <summary>
/// What this API serves, which organisations the grant covers, and Xero's own answers unprocessed — the endpoints
/// to open first against the Demo Company, since the report layouts here were written from Xero's documentation.
/// </summary>
[RequireAccessToken]
[SwaggerManualToken]
public sealed class SchemaController(XeroClient client) : XeroControllerBase
{
    /// <summary>Every data source with its fields — what the plugin's descriptor list is generated from.</summary>
    [HttpGet("sources")]
    public ActionResult<ApiResponse<IReadOnlyList<XeroSource>>> GetSources() => OkResponse(XeroSources.All);

    /// <summary>The organisations the stored grant covers (<c>GET https://api.xero.com/connections</c>).</summary>
    [HttpGet("connections")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<XeroConnection>>>> GetConnectionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return OkResponse(await client.GetConnectionsAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (MapFailure(exception) is { } result)
        {
            return result;
        }
    }

    /// <summary>
    /// Xero's answer to one Accounting API GET, unprocessed and uncached — e.g. <c>raw/Reports/ProfitAndLoss?fromDate=…</c>.
    /// Read-only like everything here; the query string is passed through as given.
    /// </summary>
    [HttpGet("raw/{**path}")]
    public async Task<ActionResult<ApiResponse<JsonElement>>> GetRawAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || path.Contains("://", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal))
            {
                return BadRequest(ApiResponse<object?>.Fail("Name an Accounting API path, e.g. raw/Organisation."));
            }

            var tenant = await client.ResolveTenantAsync(RequestedTenantId, cancellationToken).ConfigureAwait(false);
            var query = Request.Query.ToDictionary(static pair => pair.Key, static pair => (string?)pair.Value.ToString(), StringComparer.Ordinal);
            var response = await client.GetRawAsync(tenant, path, query, cancellationToken).ConfigureAwait(false);

            return OkResponse(response.Root);
        }
        catch (Exception exception) when (MapFailure(exception) is { } result)
        {
            return result;
        }
    }
}
