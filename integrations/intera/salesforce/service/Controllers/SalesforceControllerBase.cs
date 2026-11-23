using System.Globalization;
using Intera.Common.Controllers;
using Intera.Common.Models.Query;
using Intera.Salesforce.Api.Infrastructure;
using Intera.Salesforce.Api.Infrastructure.Soql;
using Intera.Salesforce.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Intera.Salesforce.Api.Controllers;

/// <summary>
/// The shape of every data endpoint here: compile the query to SOQL, run it, answer with the rows.
/// </summary>
/// <remarks>
/// What this adds over <see cref="ApiControllerBase"/> is the status mapping. Three upstream
/// outcomes are not gateway failures and must not surface as the global filter's 502, because each
/// names something a person can act on:
/// <list type="bullet">
/// <item>a query the org refuses — a field the run-as user cannot see, an object the org does not
/// have enabled — is a <b>400</b>, like any other rejected query;</item>
/// <item>a sign-in Salesforce rejects is a <b>401</b>: Salesforce answered, and correctly, and what
/// is wrong is this deployment's client id or secret;</item>
/// <item>a spent API allowance is a <b>429</b> — nothing is broken, the org is simply out of calls
/// until the rolling window moves.</item>
/// </list>
/// </remarks>
public abstract class SalesforceControllerBase(ISObjectQueryService queryService) : ApiControllerBase
{
    protected async Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QuerySObjectAsync(
        SoqlObject sobject,
        QueryRequest? query,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await queryService.QueryAsync(sobject, query, cancellationToken).ConfigureAwait(false);

            if (result.TotalCount is { } total)
            {
                Response.Headers[TotalCountHeaderName] = total.ToString(CultureInfo.InvariantCulture);
            }

            return OkResponse(result.Rows);
        }
        catch (ArgumentException exception)
        {
            // Everything the SOQL builder refuses lands here: an unknown field, an aggregate carrying
            // its own filter, a Skip past Salesforce's OFFSET cap.
            return BadRequest(ApiResponse<object?>.Fail(exception.Message));
        }
        catch (SalesforceQueryException exception)
        {
            return BadRequest(ApiResponse<object?>.Fail(exception.Message));
        }
        catch (SalesforceAuthenticationException exception)
        {
            return Unauthorized(ApiResponse<object?>.Fail(exception.Message));
        }
        catch (SalesforceLimitException exception)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, ApiResponse<object?>.Fail(exception.Message));
        }
    }
}
