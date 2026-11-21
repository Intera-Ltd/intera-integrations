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
public sealed class OpportunityController(ISObjectQueryService queryService) : SalesforceControllerBase(queryService)
{
    /// <summary>
    /// The sales pipeline. <c>stage</c> is the org's own stage name and therefore org-specific;
    /// <c>is_closed</c> / <c>is_won</c> are Salesforce's derived flags off the same stage and are
    /// what a portable "won this quarter" filter should use.
    /// </summary>
    [HttpPost("query")]
    public Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        return QuerySObjectAsync(OpportunityQueryFields.Source, query, cancellationToken);
    }
}
