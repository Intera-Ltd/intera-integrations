using System.Net;

namespace Intera.Common.Infrastructure.Http;

public sealed class HttpApiException : Exception
{
    public HttpApiException(HttpStatusCode statusCode, string responseBody, string? message = null)
        : base(message ?? $"HTTP API request failed with status code {(int)statusCode} ({statusCode}).")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }

    public string ResponseBody { get; }
}
