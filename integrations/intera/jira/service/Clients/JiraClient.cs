using Intera.Common.Infrastructure.Http;
using Intera.Jira.Api.Infrastructure;
using Intera.Jira.Api.Options;
using Intera.Jira.Api.Services;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Intera.Jira.Api.Clients;

/// <summary>One Atlassian site the stored grant covers, as <c>GET /oauth/token/accessible-resources</c> lists it.</summary>
/// <param name="Id">The cloud id — the <c>{cloudId}</c> of <c>api.atlassian.com/ex/jira/{cloudId}/…</c>.</param>
public sealed record JiraSite(string Id, string Url, string? Name, IReadOnlyList<string> Scopes);

/// <summary>An answer from Jira and when it was fetched — the <c>intera_synced_at</c> of every row built from it.</summary>
public sealed record JiraResponse(JsonElement Root, DateTime FetchedAt);

/// <summary>Every record of a paged read, and when its first page was fetched.</summary>
public sealed record JiraCollection(IReadOnlyList<JsonElement> Items, DateTime FetchedAt);

/// <summary>
/// Reads one Jira Cloud site through the Atlassian API gateway: bearer token on every call, Jira's rate limiting
/// respected, answers cached briefly (<see cref="JiraResponseCache"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Limits.</b> Jira Cloud meters OAuth apps against hourly points quotas (a global pool per app and a per-site
/// pool) plus burst limits, and answers <b>429</b> with <c>Retry-After</c> (seconds) and <c>RateLimit-Reason</c>
/// when one is hit; a 503 may carry <c>Retry-After</c> too. Both are waited out — the header's delay, or Atlassian's
/// recommended backoff of 2, 4, 8, 16 s with jitter when it is absent — at most four times and never longer than
/// <c>Jira:MaxRetryAfterSeconds</c> a wait; past that the caller gets a 429. Concurrency is capped per site
/// (<c>Jira:MaxParallelRequests</c>), as Atlassian asks.
/// </para>
/// <para>
/// <b>A 401 gets exactly one refresh and retry</b>: an access token can stop working before its stated expiry, but a
/// second failure means the grant itself is gone, and retrying again would only spend refresh tokens. A connection
/// dropped before any answer also gets one retry — every call here is a read, the POSTs included (enhanced search,
/// bulk changelog, worklog list).
/// </para>
/// <para>A Singleton: it holds the per-site gates; the HTTP client comes from the factory per call.</para>
/// </remarks>
public sealed class JiraClient(
    IOptionsMonitor<JiraOptions> options,
    JiraTokenService tokens,
    JiraResponseCache cache,
    IHttpClientFactory httpClientFactory,
    ILogger<JiraClient> logger)
{
    private const int MaxRateLimitRetries = 4;

    private static readonly TimeSpan SitesLifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new(StringComparer.OrdinalIgnoreCase);

    private JiraOptions Options => options.CurrentValue;

    /// <summary>The Jira sites the stored grant covers (a Confluence-only resource is left out).</summary>
    public Task<IReadOnlyList<JiraSite>> GetSitesAsync(CancellationToken cancellationToken) =>
        cache.GetOrAddAsync<IReadOnlyList<JiraSite>>(
            $"sites|{tokens.GrantVersion}",
            SitesLifetime,
            async ct =>
            {
                var accessibleResourcesUri = HttpsUriValidator.RequireHttpsOrLoopback(Options.AccessibleResourcesUrl, "Jira:AccessibleResourcesUrl");
                var response = await SendAsync(HttpMethod.Get, accessibleResourcesUri.AbsoluteUri, null, siteId: null, ct).ConfigureAwait(false);
                var sites = new List<JiraSite>();

                var resources = response.Root.ValueKind == JsonValueKind.Array ? response.Root.EnumerateArray().ToList() : [];
                foreach (var resource in resources)
                {
                    var id = JiraJson.String(resource, "id");
                    var url = JiraJson.String(resource, "url");
                    var scopes = JiraJson.Array(resource, "scopes").Select(JiraJson.Text).OfType<string>().ToList();

                    // One resource per product the grant covers; only those carrying a Jira scope are Jira sites.
                    if (id is null || url is null || !scopes.Any(static scope => scope.Contains("jira", StringComparison.OrdinalIgnoreCase))
                        || sites.Any(site => string.Equals(site.Id, id, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    sites.Add(new JiraSite(id, url.TrimEnd('/'), JiraJson.String(resource, "name"), scopes));
                }

                return sites;
            },
            cancellationToken);

    /// <summary>
    /// The site a request reads: the caller's <c>X-Jira-Site</c>, else <c>Jira:SiteUrl</c>, else the grant's only
    /// site. Either may be the site's URL or its cloud id, and must be one the grant covers.
    /// </summary>
    public async Task<JiraSite> ResolveSiteAsync(string? requestedSite, CancellationToken cancellationToken)
    {
        var sites = await GetSitesAsync(cancellationToken).ConfigureAwait(false);

        var wanted = !string.IsNullOrWhiteSpace(requestedSite) ? requestedSite.Trim()
            : !string.IsNullOrWhiteSpace(Options.SiteUrl) ? Options.SiteUrl.Trim()
            : null;

        if (sites.Count == 0)
        {
            throw new JiraAuthorizationException(
                "The Atlassian grant covers no Jira site. Authorise again (GET /oauth/authorize-url) and choose the Jira site on the consent page.");
        }

        if (wanted is not null)
        {
            var normalised = wanted.TrimEnd('/');
            return sites.FirstOrDefault(site =>
                       string.Equals(site.Id, normalised, StringComparison.OrdinalIgnoreCase)
                       || string.Equals(site.Url, normalised, StringComparison.OrdinalIgnoreCase)
                       || string.Equals(HostOf(site.Url), HostOf(normalised), StringComparison.OrdinalIgnoreCase))
                ?? throw new JiraRequestException(
                    $"Jira site '{wanted}' is not covered by this deployment's Atlassian grant. Covered: {Describe(sites)}.");
        }

        if (sites.Count > 1)
        {
            throw new JiraRequestException(
                $"This deployment's Atlassian grant covers several Jira sites; name one in the integration instance (site URL) or in Jira:SiteUrl. Covered: {Describe(sites)}.");
        }

        return sites[0];
    }

    /// <summary>One GET, e.g. <c>rest/api/3/field</c>, cached.</summary>
    /// <param name="reference">Cache it for <c>Jira:ReferenceCacheSeconds</c> rather than <c>Jira:CacheSeconds</c>.</param>
    public Task<JiraResponse> GetAsync(
        JiraSite site,
        string path,
        IReadOnlyList<KeyValuePair<string, string>>? query,
        bool reference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(site);

        var url = BuildUrl(site, path, query);
        return cache.GetOrAddAsync(
            $"get|{url}",
            Lifetime(reference),
            ct => SendAsync(HttpMethod.Get, url, null, site.Id, ct),
            cancellationToken);
    }

    /// <summary>A POST that only reads (enhanced search, bulk changelog, worklog list) — not cached here.</summary>
    public Task<JiraResponse> PostAsync(JiraSite site, string path, object body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(site);
        return SendAsync(HttpMethod.Post, BuildUrl(site, path, null), body, site.Id, cancellationToken);
    }

    /// <summary>Runs <paramref name="load"/> once per key and lifetime, sharing it with concurrent callers.</summary>
    public Task<T> CachedAsync<T>(JiraSite site, string key, bool reference, Func<CancellationToken, Task<T>> load, CancellationToken cancellationToken)
        where T : class =>
        cache.GetOrAddAsync($"composite|{site.Id}|{key}", Lifetime(reference), load, cancellationToken);

    /// <summary>
    /// Every record of an offset-paged list (<c>startAt</c>/<c>maxResults</c>), page by page. The records are under
    /// <paramref name="itemsProperty"/> (<c>values</c> on most lists), or the answer is a bare array when it is null.
    /// The end is <c>isLast</c> where Jira sends it, else <c>total</c>, else the first empty page.
    /// </summary>
    public Task<JiraCollection> GetPagedAsync(
        JiraSite site,
        string path,
        IReadOnlyList<KeyValuePair<string, string>>? query,
        string? itemsProperty,
        int pageSize,
        bool reference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(site);

        var baseUrl = BuildUrl(site, path, query);
        return cache.GetOrAddAsync(
            $"paged|{pageSize}|{itemsProperty}|{baseUrl}",
            Lifetime(reference),
            ct => ReadPagesAsync(site, path, query, itemsProperty, pageSize, ct),
            cancellationToken);
    }

    /// <summary>
    /// Every issue a JQL search answers (<c>POST /rest/api/3/search/jql</c>), following <c>nextPageToken</c>, with only
    /// <paramref name="fields"/> on each. A search past <c>Jira:MaxIssuesPerRead</c> is refused, not truncated.
    /// </summary>
    public Task<JiraCollection> SearchIssuesAsync(
        JiraSite site,
        string jql,
        IReadOnlyList<string> fields,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(site);

        var orderedFields = fields.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return cache.GetOrAddAsync(
            $"search|{site.Id}|{jql}|{string.Join(',', orderedFields)}",
            Lifetime(reference: false),
            ct => SearchPagesAsync(site, jql, orderedFields, ct),
            cancellationToken);
    }

    /// <summary>A raw GET for the diagnostics endpoint, uncached.</summary>
    public Task<JiraResponse> GetRawAsync(JiraSite site, string path, IReadOnlyList<KeyValuePair<string, string>> query, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, BuildUrl(site, path, query), null, site.Id, cancellationToken);

    private async Task<JiraCollection> SearchPagesAsync(JiraSite site, string jql, IReadOnlyList<string> fields, CancellationToken cancellationToken)
    {
        var items = new List<JsonElement>();
        DateTime? fetchedAt = null;
        string? nextPageToken = null;

        do
        {
            var body = new Dictionary<string, object?>
            {
                ["jql"] = jql,
                ["fields"] = fields,
                ["maxResults"] = Math.Clamp(Options.SearchPageSize, 1, 5000)
            };

            if (nextPageToken is not null)
            {
                body["nextPageToken"] = nextPageToken;
            }

            var response = await SendAsync(HttpMethod.Post, BuildUrl(site, "rest/api/3/search/jql", null), body, site.Id, cancellationToken)
                .ConfigureAwait(false);
            fetchedAt ??= response.FetchedAt;

            items.AddRange(JiraJson.Array(response.Root, "issues"));
            if (items.Count > Options.MaxIssuesPerRead)
            {
                throw new JiraRequestException(
                    $"This read would return more than {Options.MaxIssuesPerRead} issues (Jira:MaxIssuesPerRead). Narrow it — a date range, a project or the instance's JQL scope.");
            }

            nextPageToken = JiraJson.String(response.Root, "nextPageToken");
            if (JiraJson.Boolean(response.Root, "isLast") == true)
            {
                nextPageToken = null;
            }
        }
        while (nextPageToken is not null);

        return new JiraCollection(items, fetchedAt ?? DateTime.UtcNow);
    }

    private async Task<JiraCollection> ReadPagesAsync(
        JiraSite site,
        string path,
        IReadOnlyList<KeyValuePair<string, string>>? query,
        string? itemsProperty,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var items = new List<JsonElement>();
        DateTime? fetchedAt = null;

        for (var startAt = 0; ;)
        {
            var pageQuery = new List<KeyValuePair<string, string>>(query ?? [])
            {
                new("startAt", startAt.ToString(CultureInfo.InvariantCulture)),
                new("maxResults", pageSize.ToString(CultureInfo.InvariantCulture))
            };

            var response = await SendAsync(HttpMethod.Get, BuildUrl(site, path, pageQuery), null, site.Id, cancellationToken).ConfigureAwait(false);
            fetchedAt ??= response.FetchedAt;

            var page = itemsProperty is null
                ? response.Root.ValueKind == JsonValueKind.Array ? [.. response.Root.EnumerateArray()] : new List<JsonElement>()
                : JiraJson.Array(response.Root, itemsProperty).ToList();
            items.AddRange(page);
            startAt += page.Count;

            if (items.Count > Options.MaxRecordsPerList)
            {
                throw new JiraRequestException(
                    $"Reading {path} would return more than {Options.MaxRecordsPerList} records (Jira:MaxRecordsPerList).");
            }

            var isLast = itemsProperty is null ? null : JiraJson.Boolean(response.Root, "isLast");
            var total = itemsProperty is null ? null : JiraJson.Long(response.Root, "total");

            var last = isLast is { } flag ? flag
                : total is { } count ? startAt >= count
                : page.Count == 0;

            if (last || page.Count == 0)
            {
                break;
            }
        }

        return new JiraCollection(items, fetchedAt ?? DateTime.UtcNow);
    }

    private async Task<JiraResponse> SendAsync(HttpMethod method, string url, object? body, string? siteId, CancellationToken cancellationToken)
    {
        var gate = gates.GetOrAdd(siteId ?? string.Empty, _ => new SemaphoreSlim(Math.Clamp(Options.MaxParallelRequests, 1, 16)));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var accessToken = await tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            var refreshed = false;
            var connectionRetried = false;
            var rateLimitRetries = 0;

            while (true)
            {
                using var request = new HttpRequestMessage(method, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if (body is not null)
                {
                    request.Content = JsonContent.Create(body);
                }

                HttpResponseMessage sent;
                try
                {
                    sent = await httpClientFactory.CreateClient(JiraTokenService.HttpClientName)
                        .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException exception) when (!connectionRetried && !cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(exception, "The connection to Jira failed for {Url}; retrying once.", url);
                    connectionRetried = true;
                    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                using var response = sent;
                var fetchedAt = DateTime.UtcNow;
                var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return new JiraResponse(Parse(text, url), fetchedAt);
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshed)
                {
                    logger.LogInformation("Jira refused the access token for {Url}; refreshing once.", url);
                    accessToken = await tokens.RefreshAfterRejectionAsync(accessToken, cancellationToken).ConfigureAwait(false);
                    refreshed = true;
                    continue;
                }

                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                {
                    var reason = response.Headers.TryGetValues("RateLimit-Reason", out var values) ? values.FirstOrDefault() : null;
                    var retryAfter = ReadRetryAfter(response);
                    var wait = retryAfter ?? Backoff(rateLimitRetries);

                    if (rateLimitRetries < MaxRateLimitRetries && wait <= TimeSpan.FromSeconds(Options.MaxRetryAfterSeconds))
                    {
                        rateLimitRetries++;
                        logger.LogWarning(
                            "Jira answered {Status} ({Reason}) on {Url}; waiting {Seconds:0.0} s.",
                            (int)response.StatusCode, reason ?? "no reason given", url, wait.TotalSeconds);
                        await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        throw new JiraRateLimitException(
                            $"Jira's rate limit ({reason ?? "unspecified"}) is exceeded; retry in {wait.TotalSeconds:0} s.",
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

    /// <summary>Atlassian's recommended backoff without a <c>Retry-After</c>: 2, 4, 8, 16 s, times 0.7–1.3.</summary>
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
            // 204 and friends: nothing to read, which is an empty object rather than a failure.
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
            throw new JiraUpstreamException($"Jira answered {new Uri(url).AbsolutePath} with something that is not JSON: {exception.Message}");
        }
    }

    private static Exception Translate(HttpStatusCode status, string body, string url)
    {
        var detail = DescribeError(body);
        var path = new Uri(url).AbsolutePath;

        return status switch
        {
            HttpStatusCode.Unauthorized => new JiraAuthorizationException(
                $"Jira refused this deployment's grant for {path} even after a refresh ({detail}). The grant may lack a scope this endpoint needs (Jira:Scopes) — authorise again: GET /oauth/authorize-url."),
            HttpStatusCode.Forbidden => new JiraAuthorizationException(
                $"Jira refused {path} ({detail}). The user who authorised this deployment lacks the Jira permission it needs, or the grant lacks the scope."),
            HttpStatusCode.BadRequest or HttpStatusCode.NotFound => new JiraRequestException($"Jira refused {path}: {detail}"),
            _ => new JiraUpstreamException($"Jira answered {(int)status} for {path}: {detail}")
        };
    }

    /// <summary>
    /// The readable part of an error body: Jira's <c>errorMessages</c> and <c>errors</c>, or the gateway's
    /// <c>message</c>, or the raw text when neither is there.
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

            parts.AddRange(JiraJson.Array(root, "errorMessages").Select(JiraJson.Text).OfType<string>());

            if (JiraJson.Object(root, "errors") is { } errors)
            {
                parts.AddRange(errors.EnumerateObject()
                    .Select(static error => JiraJson.Text(error.Value) is { } message ? $"{error.Name}: {message}" : null)
                    .OfType<string>());
            }

            if (JiraJson.String(root, "message") is { } message)
            {
                parts.Add(message);
            }

            if (parts.Count > 0)
            {
                return string.Join("; ", parts.Distinct(StringComparer.Ordinal));
            }
        }
        catch (JsonException)
        {
            // Not JSON (the gateway answers some failures in plain text or HTML).
        }

        var trimmed = body.Trim();
        return trimmed.Length <= 300 ? trimmed : $"{trimmed[..300]}…";
    }

    private string BuildUrl(JiraSite site, string path, IReadOnlyList<KeyValuePair<string, string>>? query)
    {
        var gatewayUri = HttpsUriValidator.RequireHttpsOrLoopback(Options.ApiGatewayUrl, "Jira:ApiGatewayUrl");
        var url = $"{gatewayUri.AbsoluteUri.TrimEnd('/')}/ex/jira/{Uri.EscapeDataString(site.Id)}/{path.TrimStart('/')}";

        if (query is null || query.Count == 0)
        {
            return url;
        }

        // Ordered so one query has one cache key; a repeated name (id=1&id=2) keeps its values' order.
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

    private static string HostOf(string url) =>
        Uri.TryCreate(url.Contains("://", StringComparison.Ordinal) ? url : $"https://{url}", UriKind.Absolute, out var uri) ? uri.Host : url;

    private static string Describe(IEnumerable<JiraSite> sites) =>
        string.Join(", ", sites.Select(static site => $"{site.Name} ({site.Url}, {site.Id})"));
}
