using Intera.Common.Authentication;
using Intera.Common.Controllers;
using Intera.Common.Swagger;
using Intera.Soft1.Api.Clients;
using Intera.Soft1.Api.Models;
using Intera.Soft1.Api.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Intera.Soft1.Api.Controllers;

/// <summary>
/// What this API serves, and what the Soft1 installation behind it has — the endpoints to open first on a new
/// installation, since everything here was written without one.
/// </summary>
/// <remarks>
/// <c>sources</c> is the plugin's field list. <c>reads</c> and <c>reads/{id}/probe</c> show how each set of rows is
/// read and which of its columns the installation actually has. The rest pass Soft1's own metadata services
/// through: which objects exist, their tables and fields, what a browser shows, what a record holds — enough to
/// write <c>Soft1:Reads</c> overrides without Soft1's desktop client.
/// </remarks>
[RequireAccessToken]
[SwaggerManualToken]
public sealed class SchemaController(ISoft1Client client, ISoft1Reader reader, Soft1ReadCatalog catalog) : Soft1ControllerBase
{
    /// <summary>Every data source with its fields — what the plugin's descriptor lists are generated from.</summary>
    [HttpGet("sources")]
    public ActionResult<ApiResponse<IReadOnlyList<Soft1Source>>> GetSources() => OkResponse(Soft1Sources.All);

    /// <summary>Every read with its current settings, overrides applied.</summary>
    [HttpGet("reads")]
    public ActionResult<ApiResponse<IReadOnlyCollection<Soft1Read>>> GetReads() => OkResponse(catalog.All);

    /// <summary>
    /// Runs one read over a few rows: which columns matched each field, which columns the browser has that nothing
    /// reads, and sample rows. A read Soft1 refuses answers with the refusal instead.
    /// </summary>
    [HttpGet("reads/{id}/probe")]
    public Task<ActionResult<ApiResponse<Soft1Probe>>> ProbeAsync(string id, CancellationToken cancellationToken) =>
        Run(ct => reader.ProbeAsync(id, ct), cancellationToken);

    /// <summary>The companies, branches and modules this web account may sign in to — without signing in to one.</summary>
    [HttpGet("login")]
    public Task<ActionResult<ApiResponse<IReadOnlyList<Soft1LoginTarget>>>> GetLoginTargetsAsync(CancellationToken cancellationToken) =>
        Run(client.GetLoginTargetsAsync, cancellationToken);

    /// <summary>Every business object (<c>getObjects</c>).</summary>
    [HttpGet("objects")]
    public Task<ActionResult<ApiResponse<JsonElement>>> GetObjectsAsync(CancellationToken cancellationToken) =>
        Run(ct => client.CallAsync("getObjects", new Dictionary<string, object?>(), ct), cancellationToken);

    /// <summary>An object's tables (<c>getObjectTables</c>).</summary>
    [HttpGet("objects/{obj}/tables")]
    public Task<ActionResult<ApiResponse<JsonElement>>> GetTablesAsync(string obj, CancellationToken cancellationToken) =>
        Run(ct => client.CallAsync("getObjectTables", new Dictionary<string, object?> { ["OBJECT"] = obj }, ct), cancellationToken);

    /// <summary>A table's fields within an object (<c>getTableFields</c>).</summary>
    [HttpGet("objects/{obj}/tables/{table}/fields")]
    public Task<ActionResult<ApiResponse<JsonElement>>> GetFieldsAsync(string obj, string table, CancellationToken cancellationToken) =>
        Run(ct => client.CallAsync("getTableFields", new Dictionary<string, object?> { ["OBJECT"] = obj, ["TABLE"] = table }, ct), cancellationToken);

    /// <summary>A browser's filter dialog (<c>getDialog</c>) — the filter names <c>FILTERS</c> accepts.</summary>
    [HttpGet("objects/{obj}/dialog")]
    public Task<ActionResult<ApiResponse<JsonElement>>> GetDialogAsync(string obj, [FromQuery] string? list, CancellationToken cancellationToken) =>
        Run(ct => client.CallAsync("getDialog", new Dictionary<string, object?> { ["OBJECT"] = obj, ["LIST"] = list ?? string.Empty }, ct), cancellationToken);

    /// <summary>A browser's columns and first rows.</summary>
    [HttpGet("objects/{obj}/browser")]
    public Task<ActionResult<ApiResponse<Soft1Browser>>> GetBrowserAsync(
        string obj,
        [FromQuery] string? list,
        [FromQuery] string? filters,
        [FromQuery] int? limit,
        CancellationToken cancellationToken) =>
        Run(ct => client.PeekBrowserAsync(obj, list ?? string.Empty, filters ?? string.Empty, limit ?? 10, ct), cancellationToken);

    /// <summary>One record (<c>getData</c>); without <c>locate</c> every table and field of it.</summary>
    [HttpGet("objects/{obj}/records/{key}")]
    public Task<ActionResult<ApiResponse<JsonElement>>> GetRecordAsync(
        string obj,
        string key,
        [FromQuery] string? locate,
        CancellationToken cancellationToken) =>
        Run(ct => client.GetRecordAsync(obj, key, locate ?? string.Empty, ct), cancellationToken);

    /// <summary>Rows of one table (<c>selectorFields</c>), e.g. <c>?key=COMPANY&amp;value=1000&amp;fields=WHOUSE,NAME</c>.</summary>
    [HttpGet("tables/{table}")]
    public Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, string?>>>>> GetTableAsync(
        string table,
        [FromQuery] string? key,
        [FromQuery] string? value,
        [FromQuery] string? fields,
        CancellationToken cancellationToken) =>
        Run(ct => client.ReadTableAsync(
            table,
            key ?? string.Empty,
            value ?? string.Empty,
            [.. (fields ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            ct), cancellationToken);

    private async Task<ActionResult<ApiResponse<T>>> Run<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        try
        {
            return OkResponse(await call(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (MapFailure(exception) is { } result)
        {
            return result;
        }
    }
}
