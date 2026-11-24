using Intera.Common.Controllers;
using Intera.Common.Infrastructure.Query;
using Intera.Common.Models.Query;
using Intera.Soft1.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Intera.Soft1.Api.Controllers;

/// <summary>
/// Shared shape of every data endpoint here: read from Soft1, then run the query DSL over the rows in memory.
/// </summary>
/// <remarks>
/// <para>
/// A refused sign-in, licence or module is a <b>401</b> — this deployment's Soft1 configuration, not the
/// caller's token. A refused request and a query too broad to run are a <b>400</b>.
/// </para>
/// <para>
/// <b>The query is run once over no rows before anything is read from Soft1</b>, so an unknown field or a
/// malformed <c>relativeDate</c> is refused before a browser runs. The <see cref="ArgumentException"/> catch is
/// also what turns a bad <c>relativeDate</c> thrown by <c>QueryPushdownExtractor</c> into a 400 rather than a
/// 500.
/// </para>
/// </remarks>
public abstract class Soft1ControllerBase : ApiControllerBase
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
            Soft1AuthenticationException => Unauthorized(ApiResponse<object?>.Fail(exception.Message)),
            Soft1RequestException or Soft1QueryTooBroadException or ArgumentException =>
                BadRequest(ApiResponse<object?>.Fail(exception.Message)),
            // Soft1 did not answer at all: a wrong Soft1:ServiceUrl, a firewall, the cloud's 60-second limit. Without
            // this the global filter answers a bare 500 that names none of them.
            HttpRequestException or TaskCanceledException { InnerException: TimeoutException } => StatusCode(
                StatusCodes.Status502BadGateway,
                ApiResponse<object?>.Fail($"Soft1 could not be reached at Soft1:ServiceUrl: {exception.Message}")),
            // Configuration missing on this side (no service URL): not the caller's fault, and not Soft1's.
            InvalidOperationException => StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<object?>.Fail(exception.Message)),
            _ => null
        };
    }
}
