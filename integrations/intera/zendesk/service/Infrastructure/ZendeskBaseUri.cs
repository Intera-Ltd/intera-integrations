using Intera.Zendesk.Api.Options;
using System.Text.RegularExpressions;

namespace Intera.Zendesk.Api.Infrastructure;

/// <summary>
/// The account's base URL — the host that receives the OAuth client secret, the authorization code and every access
/// token, so it is validated: a plain subdomain label, or an explicit URL that is HTTPS (HTTP only on a loopback host,
/// which is how a stub is reached in tests).
/// </summary>
public static partial class ZendeskBaseUri
{
    public static Uri Resolve(ZendeskOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            if (!Uri.TryCreate(options.BaseUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var explicitUri)
                || (explicitUri.Scheme != Uri.UriSchemeHttps && !(explicitUri.Scheme == Uri.UriSchemeHttp && explicitUri.IsLoopback)))
            {
                throw new InvalidOperationException("Zendesk:BaseUrl must be an https:// URL (http:// only for a local host), because it receives the OAuth credentials.");
            }

            return explicitUri;
        }

        var subdomain = options.Subdomain.Trim();
        if (subdomain.Length == 0)
        {
            throw new InvalidOperationException("Zendesk:Subdomain is not configured (the 'acme' of acme.zendesk.com).");
        }

        if (!SubdomainPattern().IsMatch(subdomain))
        {
            throw new InvalidOperationException($"Zendesk:Subdomain '{subdomain}' is not a subdomain: use only the part before .zendesk.com (letters, digits, hyphens).");
        }

        return new Uri($"https://{subdomain}.zendesk.com/");
    }

    [GeneratedRegex("^[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?$")]
    private static partial Regex SubdomainPattern();
}
