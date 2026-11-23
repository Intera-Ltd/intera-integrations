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
public sealed class OrderController(ISObjectQueryService queryService) : SalesforceControllerBase(queryService)
{
    /// <summary>
    /// Confirmed customer orders. Orders is a feature an org can have switched off — this endpoint
    /// then answers 400 carrying Salesforce's own <c>INVALID_TYPE</c> text, rather than an empty
    /// list that would read as "no orders".
    /// </summary>
    [HttpPost("query")]
    public Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        return QuerySObjectAsync(OrderQueryFields.Source, query, cancellationToken);
    }
}
