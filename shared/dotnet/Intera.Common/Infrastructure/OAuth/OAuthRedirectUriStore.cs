using Intera.Common.Infrastructure.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Intera.Common.Infrastructure.OAuth;

/// <summary>
/// Validates an OAuth redirect URL before it is stored or sent. Providers compare <c>redirect_uri</c> character for
/// character against registered callback URLs, so the trimmed text is returned without URI normalization.
/// </summary>
public static class OAuthRedirectUriValidator
{
    public static string Validate(string? redirectUri)
    {
        var text = redirectUri?.Trim() ?? string.Empty;

        if (!HttpsUriValidator.TryValidateHttpsOrLoopback(text, out _, out _))
        {
            throw new ArgumentException(
                $"'{text}' is not a usable redirect URL: it must be an absolute https:// URL (http:// only on a loopback host), without user info or a fragment.",
                nameof(redirectUri));
        }

        return text;
    }
}

/// <summary>
/// The redirect URL currently in effect and where it came from (<c>configuration</c>, <c>intera</c>, or <see langword="null"/>).
/// </summary>
public sealed record OAuthRedirectUriState(string? RedirectUri, string? Source)
{
    public const string ConfigurationSource = "configuration";
    public const string InteraSource = "intera";
}

/// <summary>
/// Thrown when INTERA registers a callback URL while the deployment explicitly configures a different override URL.
/// Surfaces as HTTP 409 Conflict.
/// </summary>
public sealed class OAuthRedirectUriConflictException(string message) : Exception(message);

/// <summary>
/// Persists the OAuth redirect URL that INTERA Core registered for a deployment's <c>oauth-callback</c> webhook handler
/// so restarts do not lose it.
/// </summary>
public interface IOAuthRedirectUriStore
{
    Task<string?> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(string redirectUri, CancellationToken cancellationToken = default);
}

/// <summary>
/// Stores the registered OAuth redirect URL in an atomically written JSON file alongside the persisted OAuth grant.
/// Register as a singleton so the write gate and cached value are shared across requests.
/// </summary>
public sealed class FileOAuthRedirectUriStore(Func<string> pathProvider) : IOAuthRedirectUriStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly SemaphoreSlim gate = new(1, 1);
    private string? loadedFrom;
    private string? value;

    public async Task<string?> GetAsync(CancellationToken cancellationToken = default)
    {
        var path = ResolvePath();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (string.Equals(loadedFrom, path, StringComparison.Ordinal))
            {
                return value;
            }

            value = null;
            if (File.Exists(path))
            {
                await using var stream = File.OpenRead(path);
                var stored = await JsonSerializer.DeserializeAsync<StoredRedirectUri>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                value = string.IsNullOrWhiteSpace(stored?.RedirectUri) ? null : stored.RedirectUri;
            }

            loadedFrom = path;
            return value;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(string redirectUri, CancellationToken cancellationToken = default)
    {
        var validated = OAuthRedirectUriValidator.Validate(redirectUri);

        var path = ResolvePath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tempPath = $"{path}.tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        new StoredRedirectUri(validated, DateTimeOffset.UtcNow),
                        JsonOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(tempPath, path, overwrite: true);

            loadedFrom = path;
            value = validated;
        }
        finally
        {
            gate.Release();
        }
    }

    private string ResolvePath()
    {
        var path = pathProvider();
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("The OAuth redirect URI file path is not configured.");
        }

        return Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
    }

    private sealed record StoredRedirectUri(
        [property: JsonPropertyName("redirect_uri")] string RedirectUri,
        [property: JsonPropertyName("registered_at")] DateTimeOffset RegisteredAt);
}
