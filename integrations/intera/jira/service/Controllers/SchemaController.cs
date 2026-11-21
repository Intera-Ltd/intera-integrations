using Intera.Common.Authentication;
using Intera.Common.Controllers;
using Intera.Common.Swagger;
using Intera.Jira.Api.Clients;
using Intera.Jira.Api.Models;
using Intera.Jira.Api.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Intera.Jira.Api.Controllers;

/// <summary>
/// What this API serves, which sites the grant covers, the site's custom fields as columns, and Jira's own answers
/// unprocessed — the endpoints to open first against a new site, since everything here was written from Atlassian's
/// specifications.
/// </summary>
[RequireAccessToken]
[SwaggerManualToken]
public sealed class SchemaController(JiraClient client, IJiraDataService dataService) : JiraControllerBase
{
    /// <summary>Every data source with its fields — what the plugin's descriptor list is generated from.</summary>
    [HttpGet("sources")]
    public ActionResult<ApiResponse<IReadOnlyList<JiraSource>>> GetSources() => OkResponse(JiraSources.All);

    /// <summary>The Jira sites the stored grant covers (<c>GET https://api.atlassian.com/oauth/token/accessible-resources</c>).</summary>
    [HttpGet("sites")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<JiraSite>>>> GetSitesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return OkResponse(await client.GetSitesAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (MapFailure(exception) is { } result)
        {
            return result;
        }
    }

    /// <summary>
    /// The custom fields <c>issues</c> carries as <c>customfield_NNNNN</c> columns on this site — what the plugin's
    /// <c>provisionInstance</c> appends to that source's field list.
    /// </summary>
    [HttpGet("custom-fields")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<JiraCustomFieldDescriptor>>>> GetCustomFieldsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return OkResponse(await dataService.GetIssueCustomFieldsAsync(RequestedSite, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (MapFailure(exception) is { } result)
        {
            return result;
        }
    }

    /// <summary>
    /// Jira's answer to one GET under the site, unprocessed and uncached — e.g. <c>raw/rest/api/3/myself</c> or
    /// <c>raw/rest/agile/1.0/board</c>. Read-only like everything here; the query string is passed through as given.
    /// </summary>
    [HttpGet("raw/{**path}")]
    public async Task<ActionResult<ApiResponse<JsonElement>>> GetRawAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)
                || path.Contains("://", StringComparison.Ordinal)
                || path.Contains("..", StringComparison.Ordinal)
                || !path.StartsWith("rest/", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(ApiResponse<object?>.Fail("Name a Jira REST path, e.g. raw/rest/api/3/myself."));
            }

            var site = await client.ResolveSiteAsync(RequestedSite, cancellationToken).ConfigureAwait(false);
            var query = Request.Query
                .SelectMany(static pair => pair.Value.Select(value => new KeyValuePair<string, string>(pair.Key, value ?? string.Empty)))
                .ToList();
            var response = await client.GetRawAsync(site, path, query, cancellationToken).ConfigureAwait(false);

            return OkResponse(response.Root);
        }
        catch (Exception exception) when (MapFailure(exception) is { } result)
        {
            return result;
        }
    }
}
