namespace Intera.Common.Infrastructure.Http;

public abstract class ApiClientBase(IJsonHttpClient httpClient)
{
    protected IJsonHttpClient HttpClient { get; } = httpClient;

    protected static string BuildUrl(string baseUrl, string path)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("API base URL is not configured.");
        }

        return $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
    }
}
