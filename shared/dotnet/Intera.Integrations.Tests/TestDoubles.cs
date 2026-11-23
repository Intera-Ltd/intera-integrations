using System.Net;

namespace Intera.Integrations.Tests;

internal sealed class StubHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
{
    private int callCount;

    public int CallCount => Volatile.Read(ref callCount);

    public List<Uri?> RequestedUris { get; } = [];

    public List<string?> RequestBodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref callCount);
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        lock (RequestedUris)
        {
            RequestedUris.Add(request.RequestUri);
            RequestBodies.Add(body);
        }

        return await responder(request, cancellationToken).ConfigureAwait(false);
    }

    public static HttpResponseMessage Json(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    };
}

internal sealed class StubHttpClientFactory(Func<HttpClient> createClient) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => createClient();
}

internal sealed class StaticOptionsMonitor<TOptions>(TOptions initial) : Microsoft.Extensions.Options.IOptionsMonitor<TOptions>
{
    public TOptions CurrentValue { get; set; } = initial;

    public TOptions Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
}
