using Intera.Common.Controllers;
using Intera.Common.Infrastructure.Query;
using Intera.Common.Models.Query;
using Intera.Unleashed.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Intera.Unleashed.Api.Controllers;

/// <summary>
/// Shared shape of every data endpoint here: read the snapshot, then run the query DSL over it in
/// memory.
/// </summary>
/// <remarks>
/// <para>
/// The three status mappings exist so that an upstream answer which is not a gateway failure does not
/// surface as the global <c>ApiExceptionFilter</c>'s 502. A refused credential is a <b>401</b> — the
/// deployment's own Unleashed API id or key is wrong, or the account's plan has no API access, and
/// none of that is a fault of the caller's token. A refused request is a <b>400</b> carrying
/// Unleashed's own text. An exhausted call allowance is a <b>429</b>: nothing is broken, the account
/// has simply spent its budget for the period.
/// </para>
/// <para>
/// The fourth catch, <see cref="ArgumentException"/>, is not redundant with the one inside
/// <c>QueryInMemoryResponse</c>. The pushdown runs <b>before</b> the rows are loaded, and
/// <c>RelativeDateResolver</c> throws from there on a malformed <c>relativeDate</c> — an unsupported
/// <c>snapTo</c> or <c>unit</c> — so that exception never reaches the engine's own handler and would
/// surface as a 500 for what is plainly a bad request. Measured: <c>snapTo: "fortnight"</c> answered
/// 500 without it.
/// </para>
/// </remarks>
public abstract class UnleashedControllerBase : ApiControllerBase
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
            var rows = await load(cancellationToken).ConfigureAwait(false);

            return QueryInMemoryResponse(rows, query, fields);
        }
        catch (UnleashedAuthenticationException exception)
        {
            return Unauthorized(ApiResponse<object?>.Fail(exception.Message));
        }
        catch (UnleashedLimitException exception)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, ApiResponse<object?>.Fail(exception.Message));
        }
        catch (UnleashedRequestException exception)
        {
            return BadRequest(ApiResponse<object?>.Fail(exception.Message));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(ApiResponse<object?>.Fail(exception.Message));
        }
    }
}
