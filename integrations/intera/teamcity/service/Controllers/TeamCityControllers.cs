using Intera.Common.Authentication;
using Intera.Common.Controllers;
using Intera.Common.Infrastructure.Query;
using Intera.Common.Models.Query;
using Intera.Common.Swagger;
using Intera.TeamCity.Api.Infrastructure;
using Intera.TeamCity.Api.Models.Query;
using Intera.TeamCity.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Intera.TeamCity.Api.Controllers;

/// <summary>
/// Shared shape of every data endpoint here: read from TeamCity, then run the query DSL over the rows in
/// memory.
/// </summary>
/// <remarks>
/// <para>
/// The status mappings keep an upstream answer that is not a gateway failure out of the global filter's
/// 502: a refused or under-privileged TeamCity token is a <b>401</b>, a refused locator or a query too
/// broad to run is a <b>400</b>.
/// </para>
/// <para>
/// The query is run once over no rows before anything is read, as in <c>Intera.ZohoInventory.Api</c>:
/// an unknown field or a malformed <c>relativeDate</c> is refused before a fan-out over every
/// configuration's tests. The <see cref="ArgumentException"/> catch also covers
/// <c>QueryPushdownExtractor</c>, which runs ahead of the engine (see Known sharp edges).
/// </para>
/// </remarks>
public abstract class TeamCityControllerBase : ApiControllerBase
{
    protected async Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryRowsAsync(
        Func<CancellationToken, Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>>> load,
        QueryRequest? query,
        IReadOnlyDictionary<string, InMemoryQueryField> fields,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(load);

        try
        {
            new InMemoryQueryEngine(fields).Execute([], query);

            var rows = await load(cancellationToken).ConfigureAwait(false);

            return QueryInMemoryResponse(rows, query, fields);
        }
        catch (TeamCityAuthenticationException exception)
        {
            return Unauthorized(ApiResponse<object?>.Fail(exception.Message));
        }
        catch (Exception exception) when (exception is TeamCityRequestException or TeamCityQueryTooBroadException or ArgumentException)
        {
            return BadRequest(ApiResponse<object?>.Fail(exception.Message));
        }
    }
}

[RequireAccessToken]
[SwaggerManualToken]
public sealed class BuildTypeController(IBuildTypeService buildTypeService) : TeamCityControllerBase
{
    /// <summary>
    /// Build configurations in scope with the state of their newest finished build on the default branch
    /// (status, status text, test counters, dates) and the number of running and queued builds.
    /// </summary>
    [HttpPost("query")]
    public Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        return QueryRowsAsync(buildTypeService.GetRowsAsync, query, BuildTypeQueryFields.All, cancellationToken);
    }
}

[RequireAccessToken]
[SwaggerManualToken]
public sealed class TestResultController(ITestResultService testResultService) : TeamCityControllerBase
{
    /// <summary>
    /// Every test of the newest finished build (default branch) of each configuration that reported tests,
    /// one row per test with its result.
    /// </summary>
    /// <remarks>
    /// A condition on <c>project_id</c>, <c>project_name</c>, <c>build_type_id</c> or
    /// <c>build_type_name</c> is applied before any test is read, so a query about one configuration costs
    /// one call for its tests.
    /// </remarks>
    [HttpPost("query")]
    public Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        return QueryRowsAsync(
            token => testResultService.GetRowsAsync(query, token),
            query,
            TestResultQueryFields.All,
            cancellationToken);
    }
}
