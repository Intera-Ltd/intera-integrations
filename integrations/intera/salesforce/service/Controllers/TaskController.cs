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
public sealed class TaskController(ISObjectQueryService queryService) : SalesforceControllerBase(queryService)
{
    /// <summary>
    /// Tasks: calls, e-mails and to-dos. Half of what the spec calls "activities" — Events are the
    /// other half and a separate source, because the two objects carry different fields.
    /// </summary>
    [HttpPost("query")]
    public Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        return QuerySObjectAsync(TaskQueryFields.Source, query, cancellationToken);
    }
}
