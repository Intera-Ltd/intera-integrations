namespace Intera.Common.Infrastructure.Http;

public interface IJsonHttpClient
{
    Task<TResponse?> GetAsync<TResponse>(
        string url,
        string? bearerToken = null,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default);

    Task<TResponse?> PostFormAsync<TResponse>(
        string url,
        IReadOnlyDictionary<string, string> form,
        string? bearerToken = null,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default);

    Task<TResponse?> SendAsync<TResponse>(
        HttpMethod method,
        string url,
        HttpContent? content = null,
        string? bearerToken = null,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default);
}
