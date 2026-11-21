namespace Intera.Zendesk.Api.Infrastructure;

/// <summary>
/// Checks a redirect URL before it is stored. Zendesk sends the authorization code to it, so it must be HTTPS (HTTP only
/// on a loopback host, for a trial) and carry no fragment or user info. It is not normalised: Zendesk compares it with
/// the client's registered URLs character for character, so the text that was registered is the text that is sent.
/// </summary>
public static class ZendeskRedirectUri
{
    public static string Validate(string? redirectUri)
    {
        var text = redirectUri?.Trim() ?? string.Empty;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            || uri.UserInfo.Length > 0
            || uri.Fragment.Length > 0)
        {
            throw new ZendeskRequestException(
                $"'{text}' is not a usable redirect URL: it must be an absolute https:// URL (http:// only for a local host), without a fragment or user info.");
        }

        return text;
    }
}
