using Intera.BusinessCentral.Api.Models;
using Intera.BusinessCentral.Api.Options;
using Intera.BusinessCentral.Api.Services;
using Intera.Common.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace Intera.BusinessCentral.Api.Clients;

public sealed class BusinessCentralClient(
    IOptions<BusinessCentralOptions> options,
    IJsonHttpClient httpClient,
    IBusinessCentralOAuthService oauthService) : ApiClientBase(httpClient), IBusinessCentralClient
{
    private BusinessCentralOptions Options => options.Value;

    public Task<IReadOnlyList<BusinessCentralCompanyDto>> GetCompaniesAsync(CancellationToken cancellationToken = default) =>
        GetCollectionAsync<BusinessCentralCompanyDto>("companies", cancellationToken);

    public Task<IReadOnlyList<BusinessCentralCustomerDto>> GetCustomersAsync(string companyId, CancellationToken cancellationToken = default) =>
        GetCollectionAsync<BusinessCentralCustomerDto>(
            BuildCompanyScopedPath(companyId, "customers", "lastModifiedDateTime desc"),
            cancellationToken);

    public Task<IReadOnlyList<BusinessCentralVendorDto>> GetVendorsAsync(string companyId, CancellationToken cancellationToken = default) =>
        GetCollectionAsync<BusinessCentralVendorDto>(
            BuildCompanyScopedPath(companyId, "vendors", "lastModifiedDateTime desc"),
            cancellationToken);

    public Task<IReadOnlyList<BusinessCentralSalesInvoiceDto>> GetSalesInvoicesAsync(string companyId, CancellationToken cancellationToken = default) =>
        GetCollectionAsync<BusinessCentralSalesInvoiceDto>(
            BuildCompanyScopedPath(companyId, "salesInvoices", "lastModifiedDateTime desc"),
            cancellationToken);

    public Task<IReadOnlyList<BusinessCentralPurchaseInvoiceDto>> GetPurchaseInvoicesAsync(string companyId, CancellationToken cancellationToken = default) =>
        GetCollectionAsync<BusinessCentralPurchaseInvoiceDto>(
            BuildCompanyScopedPath(companyId, "purchaseInvoices", "lastModifiedDateTime desc"),
            cancellationToken);

    // entryNumber, not lastModifiedDateTime: ledger entries are immutable once posted, and entryNumber
    // is monotonically increasing, so "desc" reliably means "newest posted first" for the cap.
    public Task<IReadOnlyList<BusinessCentralGeneralLedgerEntryDto>> GetGeneralLedgerEntriesAsync(string companyId, CancellationToken cancellationToken = default) =>
        GetCollectionAsync<BusinessCentralGeneralLedgerEntryDto>(
            BuildCompanyScopedPath(companyId, "generalLedgerEntries", "entryNumber desc"),
            cancellationToken);

    /// <summary>
    /// Fetches one entity collection, following @odata.nextLink pages until either the collection is
    /// exhausted or Options.MaxRecordsPerEntity is reached. BC's $top only bounds a single page, so the
    /// loop here enforces the cap. Callers that scope by company request a stable newest-first $orderby so
    /// the cap predictably drops the oldest records rather than an arbitrary page.
    /// </summary>
    private async Task<IReadOnlyList<T>> GetCollectionAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        var accessToken = await oauthService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var maxRecords = Math.Max(1, Options.MaxRecordsPerEntity);
        var results = new List<T>();
        var baseUrl = ResolveApiBaseUrl();
        var baseUri = HttpsUriValidator.RequireHttpsOrLoopback(baseUrl, "BusinessCentral:ApiBaseUrl");
        string? url = BuildUrl(baseUrl, relativePath);

        while (url is not null && results.Count < maxRecords)
        {
            var page = await HttpClient.GetAsync<ODataCollectionResponse<T>>(
                url,
                accessToken,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (page?.Value is not { Count: > 0 })
            {
                break;
            }

            var remaining = maxRecords - results.Count;
            results.AddRange(page.Value.Count <= remaining ? page.Value : page.Value.Take(remaining));

            url = results.Count < maxRecords ? ValidateNextLink(page.NextLink, baseUri) : null;
        }

        return results;
    }

    private static string? ValidateNextLink(string? nextLink, Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(nextLink))
        {
            return null;
        }

        var nextUri = HttpsUriValidator.RequireHttpsOrLoopback(nextLink, "BusinessCentral @odata.nextLink");
        if (!string.Equals(nextUri.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(nextUri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)
            || nextUri.Port != baseUri.Port)
        {
            throw new InvalidOperationException("Business Central @odata.nextLink points to a different origin than the configured API base URL.");
        }

        return nextUri.ToString();
    }

    /// <summary>Builds a company-scoped entity path with a newest-first $orderby, e.g. companies({id})/customers?$orderby=...</summary>
    private static string BuildCompanyScopedPath(string companyId, string entity, string orderBy) =>
        $"companies({companyId})/{entity}?$orderby={Uri.EscapeDataString(orderBy)}";

    private string ResolveApiBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(Options.ApiBaseUrl))
        {
            return Options.ApiBaseUrl.Trim();
        }

        if (string.IsNullOrWhiteSpace(Options.TenantId))
        {
            throw new InvalidOperationException("BusinessCentral:TenantId is not configured.");
        }

        if (string.IsNullOrWhiteSpace(Options.Environment))
        {
            throw new InvalidOperationException("BusinessCentral:Environment is not configured.");
        }

        var tenant = Uri.EscapeDataString(Options.TenantId.Trim());
        var environment = Uri.EscapeDataString(Options.Environment.Trim());
        return $"https://api.businesscentral.dynamics.com/v2.0/{tenant}/{environment}/api/v2.0";
    }
}
