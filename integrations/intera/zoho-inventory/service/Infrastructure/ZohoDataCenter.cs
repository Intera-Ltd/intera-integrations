using Intera.ZohoInventory.Api.Options;

namespace Intera.ZohoInventory.Api.Infrastructure;

/// <summary>
/// Maps <c>ZohoInventory:DataCenter</c> onto the accounts host and the API host, which must agree.
/// </summary>
/// <remarks>
/// Zoho runs each data center as a separate installation: a client, a refresh token and an
/// organization created on <c>.eu</c> do not exist on <c>.com</c>, and the accounts server answers a
/// mismatch with a bare <c>invalid_client</c> that says nothing about the host. Deriving both hosts
/// from one value is what keeps them from being configured apart. Canada is the irregular one — its
/// accounts server is <c>zohocloud.ca</c>, not <c>zoho.ca</c>.
/// </remarks>
public static class ZohoDataCenter
{
    private static readonly Dictionary<string, (string Accounts, string Api)> Hosts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["com"] = ("https://accounts.zoho.com", "https://www.zohoapis.com"),
        ["eu"] = ("https://accounts.zoho.eu", "https://www.zohoapis.eu"),
        ["in"] = ("https://accounts.zoho.in", "https://www.zohoapis.in"),
        ["com.au"] = ("https://accounts.zoho.com.au", "https://www.zohoapis.com.au"),
        ["jp"] = ("https://accounts.zoho.jp", "https://www.zohoapis.jp"),
        ["ca"] = ("https://accounts.zohocloud.ca", "https://www.zohoapis.ca"),
        ["com.cn"] = ("https://accounts.zoho.com.cn", "https://www.zohoapis.com.cn"),
        ["sa"] = ("https://accounts.zoho.sa", "https://www.zohoapis.sa")
    };

    public static string ResolveAccountsUrl(ZohoInventoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return !string.IsNullOrWhiteSpace(options.AccountsUrl)
            ? options.AccountsUrl.TrimEnd('/')
            : Lookup(options.DataCenter).Accounts;
    }

    /// <summary>
    /// The API root used only when neither <c>ApiBaseUrl</c> nor the token's <c>api_domain</c> says
    /// otherwise.
    /// </summary>
    public static string ResolveDefaultApiBaseUrl(ZohoInventoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return $"{Lookup(options.DataCenter).Api}/inventory/v1";
    }

    private static (string Accounts, string Api) Lookup(string? dataCenter)
    {
        var key = (dataCenter ?? string.Empty).Trim().TrimStart('.');

        return Hosts.TryGetValue(key, out var hosts)
            ? hosts
            : throw new ZohoAuthenticationException(
                $"ZohoInventory:DataCenter '{dataCenter}' is not a Zoho data center. Use one of: {string.Join(", ", Hosts.Keys)}.");
    }
}
