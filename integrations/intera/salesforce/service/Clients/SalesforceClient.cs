using System.Globalization;
using System.Text.Json;
using Intera.Common.Infrastructure.Http;
using Intera.Salesforce.Api.Infrastructure;
using Intera.Salesforce.Api.Models;
using Intera.Salesforce.Api.Options;
using Intera.Salesforce.Api.Services;
using Microsoft.Extensions.Options;

namespace Intera.Salesforce.Api.Clients;

public interface ISalesforceClient
{
    /// <summary>
    /// Runs one SOQL statement and returns its records, following the query locator until either the
    /// result is exhausted or <paramref name="maxRecords"/> is reached.
    /// </summary>
    Task<IReadOnlyList<JsonElement>> QueryAsync(string soql, int maxRecords, CancellationToken cancellationToken);

    /// <summary>Runs a <c>SELECT COUNT()</c> statement and returns its <c>totalSize</c>.</summary>
    Task<long> CountAsync(string soql, CancellationToken cancellationToken);
}

/// <summary>
/// The one thing that talks to Salesforce's REST API.
/// </summary>
/// <remarks>
/// Deliberately thin: it knows how to sign a request, how to page a result and how to recover from an
/// expired session, and nothing about what a query means. Everything that decides the query text is
/// in <c>SoqlQueryBuilder</c>, and everything that reads the answer is in <c>SoqlRecordReader</c>.
/// </remarks>
public sealed class SalesforceClient(
    IOptions<SalesforceOptions> options,
    IJsonHttpClient httpClient,
    ISalesforceOAuthService oauthService,
    ILogger<SalesforceClient> logger) : ISalesforceClient
{
    private SalesforceOptions Options => options.Value;

    public async Task<IReadOnlyList<JsonElement>> QueryAsync(string soql, int maxRecords, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(soql);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRecords, 1);

        var records = new List<JsonElement>();
        var page = await InvokeAsync(
            (token, url, ct) => GetQueryPageAsync(token, BuildQueryUrl(url, soql), ct),
            cancellationToken).ConfigureAwait(false);

        while (page is not null)
        {
            if (page.Records is { Count: > 0 })
            {
                var remaining = maxRecords - records.Count;
                records.AddRange(page.Records.Count <= remaining ? page.Records : page.Records.Take(remaining));
            }

            // A LIMIT above the batch size is answered in pages, and the next one is fetched from the
            // locator rather than by re-running the query with an offset — which is what makes a
            // 10,000-row answer possible at all, given that OFFSET stops at 2000.
            if (page.Done || page.NextRecordsUrl is null || records.Count >= maxRecords)
            {
                break;
            }

            var nextUrl = page.NextRecordsUrl;
            page = await InvokeAsync(
                (token, url, ct) => GetQueryPageAsync(token, CombineInstanceUrl(url, nextUrl), ct),
                cancellationToken).ConfigureAwait(false);
        }

        return records;
    }

    public async Task<long> CountAsync(string soql, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(soql);

        var page = await InvokeAsync(
            (token, url, ct) => GetQueryPageAsync(token, BuildQueryUrl(url, soql), ct),
            cancellationToken).ConfigureAwait(false);

        // SELECT COUNT() answers with a totalSize and no records at all, which is what makes counting
        // a large filtered set cost one small response instead of the set itself.
        return page?.TotalSize ?? 0L;
    }

    private Task<SalesforceQueryResponse?> GetQueryPageAsync(string accessToken, string url, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Salesforce's own way to set the page size; there is no query-string equivalent.
            ["Sforce-Query-Options"] = $"batchSize={ClampBatchSize().ToString(CultureInfo.InvariantCulture)}"
        };

        return httpClient.GetAsync<SalesforceQueryResponse>(url, accessToken, headers, cancellationToken);
    }

    /// <summary>
    /// Runs one call against Salesforce, re-authenticating and retrying <b>once</b> if the session
    /// turned out to be dead.
    /// </summary>
    /// <remarks>
    /// The retry is not an optimisation, it is the actual expiry handling: Salesforce's token
    /// response carries no lifetime, so <c>Salesforce:AccessTokenLifetimeMinutes</c> is only a guess
    /// and a token can stop working before it runs out — the org's session timeout is shorter, an
    /// admin revoked the app, the password of the run-as user changed. Retrying more than once would
    /// turn a permanently wrong client secret into a loop against the token endpoint, which is how an
    /// integration gets an org's API allowance revoked.
    /// </remarks>
    private async Task<SalesforceQueryResponse?> InvokeAsync(
        Func<string, string, CancellationToken, Task<SalesforceQueryResponse?>> call,
        CancellationToken cancellationToken)
    {
        var token = await oauthService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await call(token.Token, token.InstanceUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpApiException exception) when (SalesforceErrors.IsExpiredSession(exception))
        {
            logger.LogInformation("Salesforce rejected the cached session; re-authenticating once.");
            oauthService.Invalidate();
        }
        catch (HttpApiException exception) when (SalesforceErrors.Translate(exception) is { } translated)
        {
            throw translated;
        }

        var refreshed = await oauthService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await call(refreshed.Token, refreshed.InstanceUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpApiException exception) when (SalesforceErrors.Translate(exception) is { } translated)
        {
            throw translated;
        }
    }

    private string BuildQueryUrl(string instanceUrl, string soql) =>
        $"{instanceUrl.TrimEnd('/')}/services/data/{ResolveApiVersion()}/query/?q={Uri.EscapeDataString(soql)}";

    /// <summary>
    /// <c>nextRecordsUrl</c> is instance-relative (<c>/services/data/v67.0/query/01g…-2000</c>), so it
    /// is completed with the same host the first page came from — never with the login host, which
    /// can be a different one.
    /// </summary>
    private static string CombineInstanceUrl(string instanceUrl, string relativeUrl) =>
        relativeUrl.Contains("://", StringComparison.Ordinal) || relativeUrl.StartsWith("http:", StringComparison.OrdinalIgnoreCase)
            ? HttpsUriValidator.RequireHttpsOrLoopback(relativeUrl, "Salesforce nextRecordsUrl").AbsoluteUri
            : $"{instanceUrl.TrimEnd('/')}/{relativeUrl.TrimStart('/')}";

    private string ResolveApiVersion()
    {
        var version = Options.ApiVersion?.Trim();
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidOperationException("Salesforce:ApiVersion is not configured.");
        }

        // Salesforce writes the version as "v67.0" in the path; accepting a bare "67.0" in
        // configuration and adding the prefix is friendlier than a 404 that says nothing.
        return version.StartsWith('v') ? version : $"v{version}";
    }

    private int ClampBatchSize() => Math.Clamp(Options.BatchSize, 200, 2000);
}
