using Intera.Common.Authentication;
using Intera.Common.Controllers;
using Intera.Common.Models.Query;
using Intera.Common.Swagger;
using Intera.Salesforce.Api.Models.Query;
using Intera.Salesforce.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Intera.Salesforce.Api.Controllers;

[RequireAccessToken]
[SwaggerManualToken]
public sealed class EventController(ISObjectQueryService queryService) : SalesforceControllerBase(queryService)
{
    /// <summary>
    /// Events: meetings and anything else with a start and an end. The counterpart of
    /// <c>/api/Task/query</c>; both share <c>activity_date</c>, <c>owner_id</c>, <c>who_id</c> and
    /// <c>what_id</c>, so a dashboard can put them side by side on those.
    /// </summary>
    [HttpPost("query")]
    public Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        return QuerySObjectAsync(EventQueryFields.Source, query, cancellationToken);
    }
}
