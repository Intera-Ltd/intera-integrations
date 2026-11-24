using Intera.Common.Infrastructure.Http;
using Intera.Xero.Api.Infrastructure;
using Intera.Xero.Api.Options;
using Intera.Xero.Api.Services;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Intera.Xero.Api.Clients;

/// <summary>One organisation the stored grant covers, as <c>GET /connections</c> lists it.</summary>
/// <param name="Id">The connection id — what disconnecting this one organisation would name.</param>
public sealed record XeroConnection(
    string Id,
    string TenantId,
    string? TenantType,
    string? TenantName,
    DateTime? CreatedDateUtc,
    DateTime? UpdatedDateUtc);

/// <summary>An answer from Xero and when it was fetched — the <c>intera_synced_at</c> of every row built from it.</summary>
public sealed record XeroResponse(JsonElement Root, DateTime FetchedAt);

/// <summary>Every record of a paged collection, and when the first page was fetched.</summary>
public sealed record XeroCollection(IReadOnlyList<JsonElement> Items, DateTime FetchedAt);

/// <summary>
/// Reads the Accounting API for one organisation: bearer token plus <c>xero-tenant-id</c> on every call, Xero's
/// rate limits respected, answers cached briefly (<see cref="XeroResponseCache"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Limits.</b> Xero allows five concurrent calls, 60 a minute and 5000 a day per organisation. Concurrency is
/// capped here (<c>Xero:MaxParallelRequests</c>). A 429 names its problem in <c>X-Rate-Limit-Problem</c>: a minute
/// limit is waited out (its <c>Retry-After</c>, up to <c>Xero:MaxRetryAfterSeconds</c>, at most three times), the
/// daily one is not — nothing inside one request can wait a day.
/// </para>
/// <para>
/// <b>A 401 gets exactly one refresh and retry</b>: an access token can stop working before its stated expiry, but
/// a second failure means the grant itself is gone, and retrying again would only spend refresh tokens. A connection
/// dropped before any answer also gets one retry — every call here is a read.
/// </para>
/// <para>A Singleton: it holds the per-organisation gates; the HTTP client comes from the factory per call.</para>
/// </remarks>
public sealed class XeroClient(
    IOptionsMonitor<XeroOptions> options,
    XeroTokenService tokens,
    XeroResponseCache cache,
    IHttpClientFactory httpClientFactory,
    ILogger<XeroClient> logger)
{
    private const int MaxRateLimitRetries = 3;

    /// <summary>What a page holds when Xero does not apply <c>pageSize</c>.</summary>
    private const int XeroDefaultPageSize = 100;

    private static readonly TimeSpan ConnectionsLifetime = TimeSpan.FromMinutes(5);

    // /connections answers in camelCase; read it that way, so the record is written out in this API's snake_case.
    private static readonly JsonSerializerOptions ConnectionJson = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new(StringComparer.OrdinalIgnoreCase);

    private XeroOptions Options => options.CurrentValue;

    /// <summary>The organisations the stored grant covers.</summary>
    public async Task<IReadOnlyList<XeroConnection>> GetConnectionsAsync(CancellationToken cancellationToken)
    {
        var key = $"connections|{tokens.GrantVersion}";
        var connections = await cache.GetOrAddAsync(
            key,
            ConnectionsLifetime,
            async ct =>
            {
                var connectionsUri = HttpsUriValidator.RequireHttpsOrLoopback(Options.ConnectionsUrl, "Xero:ConnectionsUrl");
                var response = await SendAsync(connectionsUri.AbsoluteUri, tenantId: null, ct).ConfigureAwait(false);
                return response.Root.Deserialize<List<XeroConnection>>(ConnectionJson) ?? [];
            },
            cancellationToken).ConfigureAwait(false);

        return connections;
    }

    /// <summary>
    /// The organisation a request reads: the caller's <c>X-Xero-Tenant-Id</c>, else <c>Xero:TenantId</c>, else the
    /// grant's only organisation. Whatever is named must be one the grant covers.
    /// </summary>
    public async Task<XeroConnection> ResolveTenantAsync(string? requestedTenantId, CancellationToken cancellationToken)
    {
        var connections = (await GetConnectionsAsync(cancellationToken).ConfigureAwait(false))
            .Where(static connection => string.Equals(connection.TenantType, "ORGANISATION", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var wanted = !string.IsNullOrWhiteSpace(requestedTenantId) ? requestedTenantId.Trim()
            : !string.IsNullOrWhiteSpace(Options.TenantId) ? Options.TenantId.Trim()
            : null;

        if (connections.Count == 0)
        {
            throw new XeroAuthorizationException(
                "The Xero grant covers no organisation. Authorise again (GET /oauth/authorize-url) and tick the organisation on Xero's consent page.");
        }

        if (wanted is not null)
        {
            return connections.FirstOrDefault(connection => string.Equals(connection.TenantId, wanted, StringComparison.OrdinalIgnoreCase))
                ?? throw new XeroRequestException(
                    $"Organisation '{wanted}' is not covered by this deployment's Xero grant. Covered: {Describe(connections)}.");
        }

        if (connections.Count > 1)
        {
            throw new XeroRequestException(
                $"This deployment's Xero grant covers several organisations; name one in the integration instance (tenant id) or in Xero:TenantId. Covered: {Describe(connections)}.");
        }

        return connections[0];
    }

    /// <summary>One GET against the Accounting API, e.g. <c>Organisation</c> or <c>Reports/TrialBalance</c>.</summary>
    /// <param name="reference">Cache it for <c>Xero:ReferenceCacheSeconds</c> rather than <c>Xero:CacheSeconds</c>.</param>
    public Task<XeroResponse> GetAsync(
        XeroConnection tenant,
        string path,
        IReadOnlyDictionary<string, string?>? query,
        bool reference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        var url = BuildUrl(path, query);
        return cache.GetOrAddAsync(
            $"get|{tenant.TenantId}|{url}",
            TimeSpan.FromSeconds(reference ? Options.ReferenceCacheSeconds : Options.CacheSeconds),
            ct => SendAsync(url, tenant.TenantId, ct),
            cancellationToken);
    }

    /// <summary>
    /// Every record of a paged collection (<c>Invoices</c>, <c>Contacts</c>, …), page by page at <c>Xero:PageSize</c>.
    /// Paging is also what makes Xero include line items in a list answer.
    /// </summary>
    public Task<XeroCollection> GetCollectionAsync(
        XeroConnection tenant,
        string path,
        string collection,
        IReadOnlyDictionary<string, string?>? query,
        bool paged,
        bool reference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        var baseUrl = BuildUrl(path, query);
        return cache.GetOrAddAsync(
            $"collection|{tenant.TenantId}|{paged}|{baseUrl}",
            TimeSpan.FromSeconds(reference ? Options.ReferenceCacheSeconds : Options.CacheSeconds),
            ct => paged
                ? ReadPagesAsync(tenant.TenantId, path, collection, query, ct)
                : ReadWholeAsync(tenant.TenantId, baseUrl, collection, ct),
            cancellationToken);
    }

    /// <summary>A raw GET for the diagnostics endpoint, uncached.</summary>
    public Task<XeroResponse> GetRawAsync(XeroConnection tenant, string path, IReadOnlyDictionary<string, string?> query, CancellationToken cancellationToken) =>
        SendAsync(BuildUrl(path, query), tenant.TenantId, cancellationToken);

    private async Task<XeroCollection> ReadWholeAsync(string tenantId, string url, string collection, CancellationToken cancellationToken)
    {
        var response = await SendAsync(url, tenantId, cancellationToken).ConfigureAwait(false);
        return new XeroCollection([.. XeroJson.Array(response.Root, collection)], response.FetchedAt);
    }

    private async Task<XeroCollection> ReadPagesAsync(
        string tenantId,
        string path,
        string collection,
        IReadOnlyDictionary<string, string?>? query,
        CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(Options.PageSize, 1, 1000);
        var items = new List<JsonElement>();
        DateTime? fetchedAt = null;

        for (var page = 1; ; page++)
        {
            var pageQuery = new Dictionary<string, string?>(query ?? new Dictionary<string, string?>(), StringComparer.Ordinal)
            {
                ["page"] = page.ToString(CultureInfo.InvariantCulture),
                ["pageSize"] = pageSize.ToString(CultureInfo.InvariantCulture)
            };

            var response = await SendAsync(BuildUrl(path, pageQuery), tenantId, cancellationToken).ConfigureAwait(false);
            fetchedAt ??= response.FetchedAt;

            var before = items.Count;
            items.AddRange(XeroJson.Array(response.Root, collection));
            var received = items.Count - before;

            if (items.Count > Options.MaxRowsPerRead)
            {
                throw new XeroRequestException(
                    $"Reading {collection} would return more than {Options.MaxRowsPerRead} records (Xero:MaxRowsPerRead). Narrow the date range in the filter.");
            }

            // The pagination block, where Xero sends one, says how many pages there are. Without it a short page is
            // the last — except one of exactly 100, Xero's own page size: an endpoint that ignored pageSize answers
            // that, and stopping there would cut the read short without a word. It costs one empty page at most.
            var pageCount = XeroJson.Object(response.Root, "pagination") is { } pagination
                ? XeroJson.Integer(pagination, "pageCount")
                : null;

            var last = pageCount is { } count
                ? page >= count
                : received == 0 || (received < pageSize && received != XeroDefaultPageSize);

            if (last)
            {
                break;
            }
        }

        return new XeroCollection(items, fetchedAt ?? DateTime.UtcNow);
    }

    private async Task<XeroResponse> SendAsync(string url, string? tenantId, CancellationToken cancellationToken)
    {
        var gate = gates.GetOrAdd(tenantId ?? string.Empty, _ => new SemaphoreSlim(Math.Clamp(Options.MaxParallelRequests, 1, 5)));
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
                if (tenantId is not null)
                {
                    request.Headers.Add("xero-tenant-id", tenantId);
                }

                HttpResponseMessage sent;
                try
                {
                    sent = await httpClientFactory.CreateClient(XeroTokenService.HttpClientName)
                        .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException exception) when (!connectionRetried && !cancellationToken.IsCancellationRequested)
                {
                    // A connection dropped before any answer (a reset behind Xero's load balancer). Every call here is a
                    // read, so sending it again is safe; a second failure is reported.
                    logger.LogWarning(exception, "The connection to Xero failed for {Url}; retrying once.", url);
                    connectionRetried = true;
                    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                using var response = sent;
                var fetchedAt = DateTime.UtcNow;
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return new XeroResponse(Parse(body, url), fetchedAt);
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshed)
                {
                    logger.LogInformation("Xero refused the access token for {Url}; refreshing once.", url);
                    accessToken = await tokens.RefreshAfterRejectionAsync(accessToken, cancellationToken).ConfigureAwait(false);
                    refreshed = true;
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var problem = response.Headers.TryGetValues("X-Rate-Limit-Problem", out var values) ? values.FirstOrDefault() : null;
                    var retryAfter = ReadRetryAfter(response);

                    if (!string.Equals(problem, "day", StringComparison.OrdinalIgnoreCase)
                        && rateLimitRetries < MaxRateLimitRetries
                        && (retryAfter ?? TimeSpan.FromSeconds(1)) <= TimeSpan.FromSeconds(Options.MaxRetryAfterSeconds))
                    {
                        rateLimitRetries++;
                        var wait = retryAfter ?? TimeSpan.FromSeconds(1);
                        logger.LogWarning("Xero rate limit ({Problem}) on {Url}; waiting {Seconds} s.", problem ?? "unspecified", url, wait.TotalSeconds);
                        await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw new XeroRateLimitException(
                        string.Equals(problem, "day", StringComparison.OrdinalIgnoreCase)
                            ? "Xero's daily call allowance for this organisation (5000) is spent; it resets within 24 hours."
                            : $"Xero's rate limit ({problem ?? "unspecified"}) is exceeded; retry in {retryAfter?.TotalSeconds ?? 60:0} s.",
                        retryAfter);
                }

                throw Translate(response.StatusCode, body, url);
            }
        }
        finally
        {
            gate.Release();
        }
    }

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
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new XeroUpstreamException($"Xero answered {url} with something that is not JSON: {exception.Message}");
        }
    }

    private static Exception Translate(HttpStatusCode status, string body, string url)
    {
        var detail = DescribeError(body);
        var path = new Uri(url).AbsolutePath;

        return status switch
        {
            // Xero's own wording: AuthenticationUnsuccessful (the token) / AuthorizationUnsuccessful (the organisation
            // was disconnected, or the grant lacks the scope this endpoint needs).
            HttpStatusCode.Unauthorized => new XeroAuthorizationException(
                $"Xero refused this deployment's grant for {path} even after a refresh ({detail}). Authorise again: GET /oauth/authorize-url."),
            HttpStatusCode.Forbidden => new XeroAuthorizationException(
                $"Xero refused {path} ({detail}). The organisation may have been disconnected, or the grant lacks the scope this endpoint needs — check Xero:Scopes and authorise again."),
            HttpStatusCode.BadRequest or HttpStatusCode.NotFound => new XeroRequestException(
                $"Xero refused {path}: {detail}"),
            HttpStatusCode.ServiceUnavailable => new XeroUpstreamException(
                $"Xero is unavailable for {path} (the organisation may be offline or being upgraded): {detail}"),
            _ => new XeroUpstreamException($"Xero answered {(int)status} for {path}: {detail}")
        };
    }

    /// <summary>
    /// The readable part of an error body: a validation error's messages, or the <c>Title</c>/<c>Detail</c> pair of an
    /// authorisation problem, or the raw text when neither is there.
    /// </summary>
    private static string DescribeError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "no details";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var messages = XeroJson.Array(root, "Elements")
                .SelectMany(static element => XeroJson.Array(element, "ValidationErrors"))
                .Select(static error => XeroJson.String(error, "Message"))
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var text = new StringBuilder();
            foreach (var part in new[] { XeroJson.String(root, "Title"), XeroJson.String(root, "Message"), XeroJson.String(root, "Detail") })
            {
                if (part is not null)
                {
                    text.Append(text.Length > 0 ? " — " : string.Empty).Append(part);
                }
            }

            if (messages.Count > 0)
            {
                text.Append(text.Length > 0 ? ": " : string.Empty).Append(string.Join("; ", messages));
            }

            if (text.Length > 0)
            {
                return text.ToString();
            }
        }
        catch (JsonException)
        {
            // Not JSON (Xero answers some failures in plain text or HTML).
        }

        var trimmed = body.Trim();
        return trimmed.Length <= 300 ? trimmed : $"{trimmed[..300]}…";
    }

    private string BuildUrl(string path, IReadOnlyDictionary<string, string?>? query)
    {
        string url;
        if (path.Contains("://", StringComparison.Ordinal))
        {
            url = HttpsUriValidator.RequireHttpsOrLoopback(path, "Xero request URL").AbsoluteUri;
        }
        else
        {
            var apiBaseUri = HttpsUriValidator.RequireHttpsOrLoopback(Options.ApiBaseUrl, "Xero:ApiBaseUrl");
            url = $"{apiBaseUri.AbsoluteUri.TrimEnd('/')}/{path.TrimStart('/')}";
        }

        if (query is null || query.Count == 0)
        {
            return url;
        }

        var parameters = query
            .Where(static pair => !string.IsNullOrEmpty(pair.Value))
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}");

        var text = string.Join("&", parameters);
        return text.Length == 0 ? url : $"{url}?{text}";
    }

    private static string Describe(IEnumerable<XeroConnection> connections) =>
        string.Join(", ", connections.Select(static connection => $"{connection.TenantName} ({connection.TenantId})"));
}
