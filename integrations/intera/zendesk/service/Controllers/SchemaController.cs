using Intera.Common.Authentication;
using Intera.Common.Controllers;
using Intera.Common.Swagger;
using Intera.Zendesk.Api.Clients;
using Intera.Zendesk.Api.Models;
using Intera.Zendesk.Api.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Intera.Zendesk.Api.Controllers;

/// <summary>
/// What this API serves, the account's ticket custom fields as columns, and Zendesk's own answers unprocessed — the
/// endpoints to open first against a new account, since everything here was written from Zendesk's published reference.
/// </summary>
[RequireAccessToken]
[SwaggerManualToken]
public sealed class SchemaController(ZendeskClient client, IZendeskDataService dataService) : ZendeskControllerBase
{
    /// <summary>Every data source with its fields — what the plugin's descriptor list is generated from.</summary>
    [HttpGet("sources")]
    public ActionResult<ApiResponse<IReadOnlyList<ZendeskSource>>> GetSources() => OkResponse(ZendeskSources.All);

    /// <summary>
    /// The ticket custom fields <c>tickets</c> carries as <c>custom_field_NNN</c> columns on a whole-row read
    /// (<c>Zendesk:TicketCustomFields</c>) — what the plugin's <c>provisionInstance</c> appends to that source's field list.
    /// </summary>
    [HttpGet("custom-fields")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ZendeskCustomFieldDescriptor>>>> GetCustomFieldsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return OkResponse(await dataService.GetTicketCustomFieldsAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (MapFailure(exception) is { } result)
        {
            return result;
        }
    }

    /// <summary>
    /// Zendesk's answer to one GET, unprocessed and uncached — e.g. <c>raw/api/v2/users/me</c> or
    /// <c>raw/api/v2/ticket_metrics?page[size]=1</c>. Read-only like everything here; the query string is passed through as given.
    /// </summary>
    [HttpGet("raw/{**path}")]
    public async Task<ActionResult<ApiResponse<JsonElement>>> GetRawAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)
                || path.Contains("://", StringComparison.Ordinal)
                || path.Contains("..", StringComparison.Ordinal)
                || !path.StartsWith("api/v2/", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(ApiResponse<object?>.Fail("Name a Zendesk REST path, e.g. raw/api/v2/users/me."));
            }

            var query = Request.Query
                .SelectMany(static pair => pair.Value.Select(value => new KeyValuePair<string, string>(pair.Key, value ?? string.Empty)))
                .ToList();
            var response = await client.GetRawAsync(path, query, cancellationToken).ConfigureAwait(false);

            return OkResponse(response.Root);
        }
        catch (Exception exception) when (MapFailure(exception) is { } result)
        {
            return result;
        }
    }
}
