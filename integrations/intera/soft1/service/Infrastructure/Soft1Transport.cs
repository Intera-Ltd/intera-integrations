using Intera.Common.Infrastructure.Http;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Intera.Soft1.Api.Infrastructure;

/// <summary>
/// One POST to <c>s1services</c>: a JSON body naming the <c>service</c>, answered by a JSON body carrying
/// <c>success</c>. Knows nothing about sessions; <see cref="Soft1ServiceException"/> reports a
/// <c>success: false</c> with its code, and the caller decides what that code means.
/// </summary>
public interface ISoft1Transport
{
    Task<JsonDocument> PostAsync(string serviceUrl, IReadOnlyDictionary<string, object?> body, CancellationToken cancellationToken);
}

/// <remarks>
/// <para>
/// <b>The body is decoded here rather than by <c>JsonHttpClient</c>, because of the character set.</b> Soft1 is
/// a Greek product whose server has long answered in windows-1253, and integrators on every platform convert it
/// (<c>iconv("Windows-1253", "UTF-8", …)</c> is the usual PHP line); whether a given installation says so in its
/// <c>Content-Type</c> is not documented. So a declared charset is honoured, and an undeclared or UTF-8 one is
/// decoded strictly as UTF-8 first and as windows-1253 only if that fails — a windows-1253 body with any Greek
/// letter in it is never valid UTF-8, while a pure-ASCII one reads the same either way.
/// </para>
/// <para>
/// Soft1 answers an application error with HTTP 200 and <c>success: false</c>, so the status code is only
/// trusted for transport failures.
/// </para>
/// </remarks>
public sealed class Soft1Transport(HttpClient httpClient) : ISoft1Transport
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly Encoding Greek;

    static Soft1Transport()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Greek = Encoding.GetEncoding(1253);
    }

    public async Task<JsonDocument> PostAsync(
        string serviceUrl,
        IReadOnlyDictionary<string, object?> body,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceUrl);
        ArgumentNullException.ThrowIfNull(body);

        using var request = new HttpRequestMessage(HttpMethod.Post, serviceUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var text = Decode(bytes, response.Content.Headers.ContentType?.CharSet);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpApiException(response.StatusCode, text);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            throw new Soft1RequestException(
                $"Soft1 answered '{body["service"]}' with something that is not JSON ({exception.Message}): {Excerpt(text)}");
        }

        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("success", out var success)
            && success.ValueKind == JsonValueKind.False)
        {
            var failure = new Soft1ServiceException(ReadCode(root), ReadError(root));
            document.Dispose();
            throw failure;
        }

        return document;
    }

    /// <summary>The body's text; see the class remarks for why the charset is not simply trusted.</summary>
    public static string Decode(ReadOnlySpan<byte> bytes, string? declaredCharset)
    {
        var charset = declaredCharset?.Trim().Trim('"');

        if (!string.IsNullOrEmpty(charset) && !IsUtf8(charset))
        {
            try
            {
                return Encoding.GetEncoding(charset).GetString(bytes);
            }
            catch (ArgumentException)
            {
                // An unknown charset name: fall through to detection.
            }
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Greek.GetString(bytes);
        }
    }

    private static bool IsUtf8(string charset) =>
        charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase) || charset.Equals("utf8", StringComparison.OrdinalIgnoreCase);

    private static int? ReadCode(JsonElement root)
    {
        foreach (var name in (ReadOnlySpan<string>)["errorcode", "errorCode", "code"])
        {
            if (!root.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String
                && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                return number;
            }
        }

        return null;
    }

    private static string ReadError(JsonElement root)
    {
        foreach (var name in (ReadOnlySpan<string>)["error", "message", "errormessage"])
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return value.GetString()!.Trim();
            }
        }

        return "no reason given";
    }

    private static string Excerpt(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length > 500 ? trimmed[..500] : trimmed;
    }
}
