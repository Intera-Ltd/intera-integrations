using Intera.Common.Controllers;
using Intera.Common.Infrastructure.Query;
using Intera.Common.Models.Query;
using Intera.ZohoInventory.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Intera.ZohoInventory.Api.Controllers;

/// <summary>
/// Shared shape of every data endpoint here: read from Zoho, then run the query DSL over the rows in
/// memory.
/// </summary>
/// <remarks>
/// <para>
/// The status mappings exist so an upstream answer that is not a gateway failure does not surface as
/// the global <c>ApiExceptionFilter</c>'s 502. A refused credential or a missing scope is a
/// <b>401</b> — the deployment's own Zoho configuration, not the caller's token. A refused request
/// and a query too broad to run are a <b>400</b>. A spent call limit is a <b>429</b>.
/// </para>
/// <para>
/// The <see cref="ArgumentException"/> catch is not redundant with the one inside
/// <c>QueryInMemoryResponse</c>: the document sources run <c>QueryPushdownExtractor</c> before any
/// row is loaded, and a malformed <c>relativeDate</c> throws from there; catching it turns that into a 400.
/// </para>
/// <para>
/// <b>The query is run once over no rows before anything is read from Zoho.</b> The engine builds
/// its filter up front, so an unknown field, a malformed relative date or an out-of-range
/// <c>top</c> is refused there — and a request that was always going to be a 400 does not spend a
/// few hundred calls of the organization's daily allowance on the way to it.
/// </para>
/// </remarks>
public abstract class ZohoControllerBase : ApiControllerBase
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
        catch (Exception exception) when (MapFailure(exception) is { } result)
        {
            return result;
        }
    }

    /// <summary>The same mapping for the non-query endpoints.</summary>
    protected ObjectResult? MapFailure(Exception exception)
    {
        return exception switch
        {
            ZohoAuthenticationException => Unauthorized(ApiResponse<object?>.Fail(exception.Message)),
            ZohoLimitException => StatusCode(StatusCodes.Status429TooManyRequests, ApiResponse<object?>.Fail(exception.Message)),
            ZohoRequestException or ZohoQueryTooBroadException or ArgumentException => BadRequest(ApiResponse<object?>.Fail(exception.Message)),
            _ => null
        };
    }
}
