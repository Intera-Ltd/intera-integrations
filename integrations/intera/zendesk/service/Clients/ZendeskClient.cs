using Intera.Zendesk.Api.Infrastructure;
using Intera.Zendesk.Api.Options;
using Intera.Zendesk.Api.Services;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Intera.Zendesk.Api.Clients;

/// <summary>An answer from Zendesk and when it was fetched — the <c>intera_synced_at</c> of every row built from it.</summary>
public sealed record ZendeskResponse(JsonElement Root, DateTime FetchedAt);

/// <summary>
/// Every record of a paged or streamed read, and when its first page was fetched. <paramref name="Extra"/> holds the
/// records of a sideloaded array (the <c>metric_sets</c> of an incremental ticket export) when one was asked for.
/// </summary>
public sealed record ZendeskCollection(IReadOnlyList<JsonElement> Items, IReadOnlyList<JsonElement> Extra, DateTime FetchedAt);

/// <summary>A 404 on a per-record read: the ticket does not exist (or is deleted), which a caller may read as "no rows".</summary>
public sealed class ZendeskNotFoundException(string message) : ZendeskRequestException(message);

/// <summary>
/// Reads one Zendesk account's REST API: an OAuth bearer token on every call, rate limiting respected, answers
/// cached briefly (<see cref="ZendeskResponseCache"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Credential.</b> The access token of the deployment's OAuth grant (<see cref="ZendeskTokenService"/>), refreshed
/// when it nears expiry; a 401 before that gets exactly one refresh and retry. The token is sent only to the configured
/// host — a <c>next</c> link that points anywhere else is refused rather than followed.
/// </para>
/// <para>
/// <b>Paging</b> follows whichever the endpoint speaks: cursor pagination (<c>meta.has_more</c> + <c>links.next</c>, asked
/// for with <c>page[size]</c>), offset pagination (<c>next_page</c>), or an incremental export stream
/// (<c>after_url</c> / <c>next_page</c> until <c>end_of_stream</c>).
/// </para>
/// <para>
/// <b>Limits.</b> Zendesk meters requests per minute by plan (and the incremental exports at 10 a minute) and answers
/// <b>429</b> with <c>Retry-After</c> in seconds. That is waited out — at most four times and never longer than
/// <c>Zendesk:MaxRetryAfterSeconds</c> a wait; past that the caller gets a 429. Concurrency is capped
/// (<c>Zendesk:MaxParallelRequests</c>). Every call here is a read, so a connection dropped before any answer gets one retry.
/// </para>
/// <para>A Singleton: it holds the gate; the HTTP client comes from the factory per call.</para>
/// </remarks>
public sealed class ZendeskClient(
    IOptionsMonitor<ZendeskOptions> options,
    ZendeskTokenService tokens,
    ZendeskResponseCache cache,
    IHttpClientFactory httpClientFactory,
    ILogger<ZendeskClient> logger)
{
    public const string HttpClientName = "Zendesk";

    private const int MaxRateLimitRetries = 4;

    private readonly SemaphoreSlim gate = new(Math.Clamp(options.CurrentValue.MaxParallelRequests, 1, 16));

    private ZendeskOptions Options => options.CurrentValue;

    /// <summary>The account's base URL, validated (see <see cref="ZendeskBaseUri"/>).</summary>
    public Uri BaseUri => ZendeskBaseUri.Resolve(Options);

    /// <summary>One GET, e.g. <c>api/v2/ticket_forms</c>, cached.</summary>
    /// <param name="reference">Cache it for <c>Zendesk:ReferenceCacheSeconds</c> rather than <c>Zendesk:CacheSeconds</c>.</param>
    public Task<ZendeskResponse> GetAsync(
        string path,
        IReadOnlyList<KeyValuePair<string, string>>? query,
        bool reference,
        CancellationToken cancellationToken)
    {
        var url = BuildUrl(path, query);
        return cache.GetOrAddAsync($"get|{url}", Lifetime(reference), ct => SendAsync(url, ct), cancellationToken);
    }

    /// <summary>Runs <paramref name="load"/> once per key and lifetime, sharing it with concurrent callers.</summary>
    public Task<T> CachedAsync<T>(string key, bool reference, Func<CancellationToken, Task<T>> load, CancellationToken cancellationToken)
        where T : class =>
        cache.GetOrAddAsync($"composite|{BaseUri}|{key}", Lifetime(reference), load, cancellationToken);

    /// <summary>
    /// Every record of a list read page by page. The records are under <paramref name="itemsProperty"/>. Cursor
    /// pagination is asked for (<c>page[size]</c>); an endpoint that only knows offset pagination ignores that and its
    /// <c>next_page</c> is followed instead.
    /// </summary>
    public Task<ZendeskCollection> GetPagedAsync(
        string path,
        IReadOnlyList<KeyValuePair<string, string>>? query,
        string itemsProperty,
        int pageSize,
        bool reference,
        CancellationToken cancellationToken)
    {
        var pageQuery = new List<KeyValuePair<string, string>>(query ?? []) { new("page[size]", pageSize.ToString(CultureInfo.InvariantCulture)) };
        var url = BuildUrl(path, pageQuery);

        return cache.GetOrAddAsync(
            $"paged|{itemsProperty}|{url}",
            Lifetime(reference),
            ct => ReadAsync(url, itemsProperty, null, NextOfPaged, stop: null, ct),
            cancellationToken);
    }

    /// <summary>
    /// Every record of an incremental export stream (<c>incremental/tickets/cursor</c>,
    /// <c>incremental/ticket_metric_events</c>), until Zendesk says <c>end_of_stream</c> or <paramref name="stop"/>
    /// says the page just read went past what is wanted (the stream is ascending in time).
    /// </summary>
    public Task<ZendeskCollection> GetStreamAsync(
        string path,
        IReadOnlyList<KeyValuePair<string, string>> query,
        string itemsProperty,
        string? extraProperty,
        string? stopKey,
        Func<IReadOnlyList<JsonElement>, bool>? stop,
        CancellationToken cancellationToken)
    {
        var url = BuildUrl(path, query);

        return cache.GetOrAddAsync(
            $"stream|{itemsProperty}|{extraProperty}|{stopKey}|{url}",
            Lifetime(reference: false),
            ct => ReadAsync(url, itemsProperty, extraProperty, NextOfStream, stop, ct),
            cancellationToken);
    }

    /// <summary>A raw GET for the diagnostics endpoint, uncached.</summary>
    public Task<ZendeskResponse> GetRawAsync(string path, IReadOnlyList<KeyValuePair<string, string>> query, CancellationToken cancellationToken) =>
        SendAsync(BuildUrl(path, query), cancellationToken);

    /// <summary>One GET that reads a missing record (404) as null — a ticket deleted since the list that named it.</summary>
    public async Task<ZendeskResponse?> TryGetAsync(string path, IReadOnlyList<KeyValuePair<string, string>>? query, CancellationToken cancellationToken)
    {
        try
        {
            return await GetAsync(path, query, reference: false, cancellationToken).ConfigureAwait(false);
        }
        catch (ZendeskNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Like <see cref="GetPagedAsync"/> for one ticket's sub-resource (comments, metric events): an absent ticket reads as no records.</summary>
    public async Task<ZendeskCollection> TryGetPagedAsync(
        string path,
        string itemsProperty,
        int pageSize,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GetPagedAsync(path, null, itemsProperty, pageSize, reference: false, cancellationToken).ConfigureAwait(false);
        }
        catch (ZendeskNotFoundException)
        {
            return new ZendeskCollection([], [], DateTime.UtcNow);
        }
    }

    private delegate string? NextLink(JsonElement root);

    /// <summary>Cursor pagination (<c>meta.has_more</c> + <c>links.next</c>), else offset pagination (<c>next_page</c>).</summary>
    private static string? NextOfPaged(JsonElement root)
    {
        if (ZendeskJson.Object(root, "meta") is { } meta && ZendeskJson.Boolean(meta, "has_more") is { } hasMore)
        {
            return hasMore ? ZendeskJson.String(ZendeskJson.Object(root, "links"), "next") : null;
        }

        return ZendeskJson.String(root, "next_page");
    }

    /// <summary>An incremental export: <c>after_url</c> (cursor) or <c>next_page</c> (time based), until <c>end_of_stream</c>.</summary>
    private static string? NextOfStream(JsonElement root) =>
        ZendeskJson.Boolean(root, "end_of_stream") == true
            ? null
            : ZendeskJson.String(root, "after_url") ?? ZendeskJson.String(root, "next_page");

    private async Task<ZendeskCollection> ReadAsync(
        string firstUrl,
        string itemsProperty,
        string? extraProperty,
        NextLink nextOf,
        Func<IReadOnlyList<JsonElement>, bool>? stop,
        CancellationToken cancellationToken)
    {
        var items = new List<JsonElement>();
        var extra = new List<JsonElement>();
        DateTime? fetchedAt = null;
        var url = firstUrl;

        while (true)
        {
            var response = await SendAsync(url, cancellationToken).ConfigureAwait(false);
            fetchedAt ??= response.FetchedAt;

            var page = ZendeskJson.Array(response.Root, itemsProperty).ToList();
            items.AddRange(page);
            if (extraProperty is not null)
            {
                extra.AddRange(ZendeskJson.Array(response.Root, extraProperty));
            }

            if (items.Count > Options.MaxRecordsPerRead)
            {
                throw new ZendeskRequestException(
                    $"This read would return more than {Options.MaxRecordsPerRead} records (Zendesk:MaxRecordsPerRead). Narrow it — a date range, an id, or a role.");
            }

            var next = nextOf(response.Root);
            if (next is null || page.Count == 0 || (stop is not null && stop(page)))
            {
                break;
            }

            url = FollowOnlyOurHost(next);
        }

        return new ZendeskCollection(items, extra, fetchedAt ?? DateTime.UtcNow);
    }

    /// <summary>A link Zendesk sends back must stay on the configured host: the next request carries the credential.</summary>
    private string FollowOnlyOurHost(string next)
    {
        if (!Uri.TryCreate(BaseUri, next, out var uri)
            || !string.Equals(uri.Host, BaseUri.Host, StringComparison.OrdinalIgnoreCase)
            || uri.Port != BaseUri.Port
            || uri.Scheme != BaseUri.Scheme)
        {
            throw new ZendeskUpstreamException($"Zendesk sent a page link to another host ({next}); it was not followed, because the request carries the API credential.");
        }

        return uri.AbsoluteUri;
    }

    private async Task<ZendeskResponse> SendAsync(string url, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var accessToken = await tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            var refreshed = false;
            var connectionRetried = false;
            var rateLimitRetries = 0;

            while (true)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage sent;
                try
                {
                    sent = await httpClientFactory.CreateClient(HttpClientName)
                        .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException exception) when (!connectionRetried && !cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(exception, "The connection to Zendesk failed for {Url}; retrying once.", url);
                    connectionRetried = true;
                    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                using var response = sent;
                var fetchedAt = DateTime.UtcNow;
                var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return new ZendeskResponse(Parse(text, url), fetchedAt);
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshed)
                {
                    logger.LogInformation("Zendesk refused the access token for {Url}; refreshing once.", url);
                    accessToken = await tokens.RefreshAfterRejectionAsync(accessToken, cancellationToken).ConfigureAwait(false);
                    refreshed = true;
                    continue;
                }

                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                {
                    var retryAfter = ReadRetryAfter(response);
                    var wait = retryAfter ?? Backoff(rateLimitRetries);

                    if (rateLimitRetries < MaxRateLimitRetries && wait <= TimeSpan.FromSeconds(Options.MaxRetryAfterSeconds))
                    {
                        rateLimitRetries++;
                        logger.LogWarning(
                            "Zendesk answered {Status} on {Url}; waiting {Seconds:0.0} s.",
                            (int)response.StatusCode, url, wait.TotalSeconds);
                        await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        throw new ZendeskRateLimitException(
                            $"Zendesk's rate limit is exceeded; retry in {wait.TotalSeconds:0} s.",
                            wait);
                    }
                }

                throw Translate(response.StatusCode, text, url);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>A backoff without a <c>Retry-After</c>: 2, 4, 8, 16 s, times 0.7–1.3.</summary>
    private static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Pow(2, attempt + 1) * (0.7 + (Random.Shared.NextDouble() * 0.6)));

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
    }

    private static JsonElement Parse(string body, string url)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            using var empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new ZendeskUpstreamException($"Zendesk answered {new Uri(url).AbsolutePath} with something that is not JSON: {exception.Message}");
        }
    }

    internal static Exception Translate(HttpStatusCode status, string body, string url)
    {
        var detail = DescribeError(body);
        var path = new Uri(url).AbsolutePath;

        return status switch
        {
            HttpStatusCode.Unauthorized => new ZendeskAuthorizationException(
                $"Zendesk refused this deployment's grant for {path} even after a refresh ({detail}). The grant may be revoked or expired, or lack a scope — authorise again: GET /oauth/authorize-url."),
            HttpStatusCode.Forbidden => new ZendeskAuthorizationException(
                $"Zendesk refused {path} ({detail}). The user who authorised this deployment lacks the role for it (agents read tickets, admins read the configuration), or the account's plan does not include the feature."),
            HttpStatusCode.NotFound => new ZendeskNotFoundException($"Zendesk has no {path} ({detail})."),
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => new ZendeskRequestException($"Zendesk refused {path}: {detail}"),
            _ => new ZendeskUpstreamException($"Zendesk answered {(int)status} for {path}: {detail}")
        };
    }

    /// <summary>
    /// The readable part of an error body: <c>error</c> as text or as <c>{title, message}</c>, <c>description</c>,
    /// <c>errors[].title/detail</c>, or the raw text when none is there.
    /// </summary>
    internal static string DescribeError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "no details";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var parts = new List<string>();

            if (ZendeskJson.Property(root, "error") is { } error)
            {
                if (error.ValueKind == JsonValueKind.Object)
                {
                    parts.AddRange(new[] { ZendeskJson.String(error, "title"), ZendeskJson.String(error, "message") }.OfType<string>());
                }
                else if (ZendeskJson.Text(error) is { } errorText)
                {
                    parts.Add(errorText);
                }
            }

            if (ZendeskJson.String(root, "description") is { } description)
            {
                parts.Add(description);
            }

            foreach (var item in ZendeskJson.Array(root, "errors"))
            {
                var title = ZendeskJson.String(item, "title");
                var detail = ZendeskJson.String(item, "detail");
                if (title is not null || detail is not null)
                {
                    parts.Add(string.Join(": ", new[] { title, detail }.OfType<string>()));
                }
            }

            if (parts.Count > 0)
            {
                return string.Join("; ", parts.Distinct(StringComparer.Ordinal));
            }
        }
        catch (JsonException)
        {
            // Not JSON (a gateway answers some failures in plain text or HTML).
        }

        var trimmed = body.Trim();
        return trimmed.Length <= 300 ? trimmed : $"{trimmed[..300]}…";
    }

    private string BuildUrl(string path, IReadOnlyList<KeyValuePair<string, string>>? query)
    {
        var url = new Uri(BaseUri, path.TrimStart('/')).AbsoluteUri;

        if (query is null || query.Count == 0)
        {
            return url;
        }

        // Ordered so one query has one cache key; a repeated name (role[]=agent&role[]=admin) keeps its values' order.
        var parameters = query
            .Where(static pair => !string.IsNullOrEmpty(pair.Value))
            .Select((pair, index) => (pair, index))
            .OrderBy(static item => item.pair.Key, StringComparer.Ordinal)
            .ThenBy(static item => item.index)
            .Select(static item => $"{Uri.EscapeDataString(item.pair.Key)}={Uri.EscapeDataString(item.pair.Value)}");

        var text = string.Join("&", parameters);
        return text.Length == 0 ? url : $"{url}?{text}";
    }

    private TimeSpan Lifetime(bool reference) =>
        TimeSpan.FromSeconds(reference ? Options.ReferenceCacheSeconds : Options.CacheSeconds);

}
