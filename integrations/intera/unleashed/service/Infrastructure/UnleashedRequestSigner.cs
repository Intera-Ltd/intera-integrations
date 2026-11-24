using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Intera.Unleashed.Api.Infrastructure;

/// <summary>
/// Builds the query string of an Unleashed request and the signature that authenticates it.
/// </summary>
/// <remarks>
/// <para>
/// Unleashed signs <b>the query string and nothing else</b>: HMAC-SHA256 over the characters after
/// the <c>?</c> (an empty string when there are none), keyed with the account's API key, Base64
/// encoded, sent as <c>api-auth-signature</c>. The page number is part of the <i>path</i>
/// (<c>/SalesOrders/3?pageSize=200</c>) and therefore is not signed — a detail worth knowing, because
/// getting it wrong produces a 403 that says nothing.
/// </para>
/// <para>
/// The one thing the documentation does not settle is how a value that needs percent-encoding is
/// signed: as written in the URL, or as decoded on the far side. The two agree for every parameter
/// this project sends, and that is by construction rather than by luck — <see cref="Encode"/> leaves
/// the characters those values actually contain (letters, digits, <c>-</c>, <c>.</c>, <c>:</c>,
/// <c>,</c>) untouched, so a date, a page size, a status list and an ordinary code are byte-identical
/// either way. It only becomes a question for a value carrying a space or a reserved character —
/// a warehouse code with a space in it, say — and that is where a 403 would first appear.
/// </para>
/// </remarks>
public static class UnleashedRequestSigner
{
    /// <summary>
    /// Joins parameters into the query string that goes into the URL <b>and</b> into the signature.
    /// Both come from this one string on purpose: two spellings of the same parameters is exactly how
    /// a signature stops matching.
    /// </summary>
    /// <remarks>
    /// Parameters with a null or blank value are dropped rather than sent empty — an empty
    /// <c>warehouseCode=</c> is not the same request as no <c>warehouseCode</c> at all.
    /// </remarks>
    public static string BuildQueryString(IEnumerable<KeyValuePair<string, string?>> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var builder = new StringBuilder();

        foreach (var (name, value) in parameters)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('&');
            }

            builder.Append(Encode(name)).Append('=').Append(Encode(value));
        }

        return builder.ToString();
    }

    /// <summary>
    /// The <c>api-auth-signature</c> header value for <paramref name="queryString"/>: Base64 of
    /// HMAC-SHA256, keyed with the API key, over the UTF-8 bytes of the query string.
    /// </summary>
    public static string Sign(string queryString, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(queryString);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new UnleashedAuthenticationException("Unleashed:ApiKey is not configured.");
        }

        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(apiKey), Encoding.UTF8.GetBytes(queryString));

        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Percent-encodes one query-string component, leaving alone the characters that are legal
    /// unencoded in a query and that Unleashed's own parameters use: the unreserved set plus
    /// <c>:</c> and <c>,</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="Uri.EscapeDataString"/> is deliberately not used. It escapes <c>:</c> and <c>,</c>
    /// as well, which would turn <c>modifiedSince=2026-01-01T00:00:00</c> and
    /// <c>orderStatus=Placed,Completed</c> into spellings no other Unleashed client sends — and since
    /// the signature covers this exact text, an encoding nobody else uses is the one thing most likely
    /// to disagree with what the server verifies.
    /// </remarks>
    private static string Encode(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var c = (char)b;

            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~' or ':' or ',')
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }
}
