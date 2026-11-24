using Intera.Common.Authentication;
using Intera.Common.Controllers;
using Intera.Common.Infrastructure.Query;
using Intera.Common.Models.Query;
using Intera.Common.Swagger;
using Intera.Eurobank.Api.Clients;
using Intera.Eurobank.Api.Infrastructure;
using Intera.Eurobank.Api.Models.Query;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Intera.Eurobank.Api.Controllers;

[RequireAccessToken]
[SwaggerManualToken]
public sealed class EurobankController(IEurobankAccountsClient eurobankClient) : ApiControllerBase
{
    [HttpPost("accounts/query")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryAccountsAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        try
        {
            new InMemoryQueryEngine(EurobankAccountQueryFields.All).Execute([], query);

            var accounts = await eurobankClient.GetAccountsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            var rows = accounts.Select(account => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["account_name"] = account.AccountName,
                ["account_type"] = account.AccountType,
                ["account_type_description"] = account.AccountTypeDescription,
                ["iban"] = account.Iban,
                ["account_number"] = account.AccountNumber,
                ["currency"] = account.Currency,
                ["available_balance"] = account.Balances?.Available,
                ["current_balance"] = account.Balances?.Current,
                ["status"] = account.Status
            });

            return QueryInMemoryResponse(rows, query, EurobankAccountQueryFields.All);
        }
        catch (EurobankAuthorizationException exception)
        {
            return Unauthorized(ApiResponse<object?>.Fail(exception.Message));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(ApiResponse<object?>.Fail(exception.Message));
        }
    }
}
