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
public sealed class AccountController(ISObjectQueryService queryService) : SalesforceControllerBase(queryService)
{
    /// <summary>
    /// Accounts — the customers and companies the org sells to, and the record every other source
    /// here links back to through <c>account_id</c>. Filters, ordering and paging are compiled into
    /// the SOQL <c>WHERE</c>, so a query over a large org costs one request, not a scan.
    /// </summary>
    [HttpPost("query")]
    public Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        return QuerySObjectAsync(AccountQueryFields.Source, query, cancellationToken);
    }
}
