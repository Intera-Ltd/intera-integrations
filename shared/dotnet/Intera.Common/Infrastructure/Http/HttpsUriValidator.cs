using System.Diagnostics.CodeAnalysis;

namespace Intera.Common.Infrastructure.Http;

/// <summary>
/// Validates outbound URLs that carry credentials, authorization codes, or bearer tokens.
/// Plaintext <c>http://</c> is permitted only on loopback hosts for local development and tests.
/// </summary>
public static class HttpsUriValidator
{
    public static Uri RequireHttpsOrLoopback(string? url, string settingName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingName);

        if (!TryValidateHttpsOrLoopback(url, out var uri, out var reason))
        {
            throw new InvalidOperationException($"{settingName} {reason}");
        }

        return uri;
    }

    public static bool TryValidateHttpsOrLoopback(
        string? url,
        [NotNullWhen(true)] out Uri? uri,
        [NotNullWhen(false)] out string? reason)
    {
        var text = url?.Trim() ?? string.Empty;

        if (text.Length == 0 || !Uri.TryCreate(text, UriKind.Absolute, out var parsed))
        {
            uri = null;
            reason = "must be an absolute https:// URL (or http:// on a loopback host).";
            return false;
        }

        var allowedScheme = parsed.Scheme == Uri.UriSchemeHttps
            || (parsed.Scheme == Uri.UriSchemeHttp && parsed.IsLoopback);

        if (!allowedScheme)
        {
            uri = null;
            reason = "must use https:// (http:// is allowed only on a loopback host), because it receives credentials.";
            return false;
        }

        if (parsed.UserInfo.Length > 0)
        {
            uri = null;
            reason = "must not contain user info in the URL.";
            return false;
        }

        if (parsed.Fragment.Length > 0)
        {
            uri = null;
            reason = "must not contain a URL fragment.";
            return false;
        }

        uri = parsed;
        reason = null;
        return true;
    }
}
