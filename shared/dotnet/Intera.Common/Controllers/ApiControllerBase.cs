using Intera.Common.Authentication;
using Intera.Common.Infrastructure.Query;
using Intera.Common.Models.Query;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace Intera.Common.Controllers;

[ApiController]
[Route("api/[controller]")]
public abstract class ApiControllerBase : ControllerBase
{
    public const string TotalCountHeaderName = "X-Total-Count";

    protected long AccessTokenId
    {
        get
        {
            var value = User.FindFirst(AccessTokenAuthenticationDefaults.TokenIdClaimType)?.Value;
            if (!long.TryParse(value, CultureInfo.InvariantCulture, out var tokenId))
            {
                throw new InvalidOperationException("Access token id is not available. Ensure the endpoint requires access token authentication.");
            }

            return tokenId;
        }
    }

    protected ActionResult<ApiResponse<T>> OkResponse<T>(T data)
    {
        return Ok(ApiResponse<T>.Success(data));
    }

    protected ActionResult<ApiResponse<object?>> OkResponse()
    {
        return Ok(ApiResponse<object?>.Success(null));
    }

    protected ActionResult<ApiResponse<object?>> BadRequestResponse(string message)
    {
        return BadRequest(ApiResponse<object?>.Fail(message));
    }

    protected ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>> QueryInMemoryResponse(
        IEnumerable<IReadOnlyDictionary<string, object?>> source,
        QueryRequest? query,
        IReadOnlyDictionary<string, InMemoryQueryField> fields)
    {
        try
        {
            var engine = new InMemoryQueryEngine(fields);
            var rows = engine.Execute(source, query);

            if (query?.Skip is not null)
            {
                Response.Headers[TotalCountHeaderName] =
                    engine.FilteredRowCount.ToString(CultureInfo.InvariantCulture);
            }

            return OkResponse(rows);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(ApiResponse<object?>.Fail(exception.Message));
        }
    }
}

public sealed record ApiResponse<T>(bool Ok, T? Data, string? Error)
{
    public static ApiResponse<T> Success(T? data) => new(true, data, null);

    public static ApiResponse<T> Fail(string error) => new(false, default, error);
}
