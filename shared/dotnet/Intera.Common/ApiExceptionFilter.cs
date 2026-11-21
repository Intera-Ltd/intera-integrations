using Intera.Common.Infrastructure.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Intera.Common;

public sealed class ApiExceptionFilter(ILogger<ApiExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        context.Result = context.Exception switch
        {
            HttpApiException exception => CreateHttpApiErrorResult(exception),
            _ => CreateUnhandledErrorResult(context.Exception)
        };

        context.ExceptionHandled = true;
    }

    private ObjectResult CreateHttpApiErrorResult(HttpApiException exception)
    {
        logger.LogError(exception, "External HTTP API request failed with status code {StatusCode}.", exception.StatusCode);

        return new ObjectResult(new
        {
            ok = false,
            error = "External API request failed.",
            statusCode = (int)exception.StatusCode
        })
        {
            StatusCode = StatusCodes.Status502BadGateway
        };
    }

    private ObjectResult CreateUnhandledErrorResult(Exception exception)
    {
        logger.LogError(exception, "Unhandled API exception.");

        return new ObjectResult(new
        {
            ok = false,
            error = "Internal server error."
        })
        {
            StatusCode = StatusCodes.Status500InternalServerError
        };
    }
}
