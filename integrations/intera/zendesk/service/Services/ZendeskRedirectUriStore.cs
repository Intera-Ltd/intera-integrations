using Intera.Zendesk.Api.Options;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Intera.Zendesk.Api.Services;

/// <summary>
/// The redirect URL INTERA registered for this deployment — the public webhook address Core gives the plugin's
/// <c>oauth-callback</c> handler — kept so a restart does not lose it.
/// </summary>
public interface IZendeskRedirectUriStore
{
    Task<string?> GetAsync(CancellationToken cancellationToken);

    Task SaveAsync(string redirectUri, CancellationToken cancellationToken);
}

/// <summary>
/// Keeps the registered redirect URL in a small JSON file next to the grant (<c>Zendesk:RedirectUriFilePath</c>, in the
/// mounted <c>data/</c>). Core registers the address once, when the instance is saved, and does not repeat it on its own,
/// so it has to survive a redeploy as the grant does. A Singleton: the value is read from disk once, then held.
/// </summary>
public sealed class FileZendeskRedirectUriStore(IOptionsMonitor<ZendeskOptions> options) : IZendeskRedirectUriStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly SemaphoreSlim gate = new(1, 1);
    private string? loadedFrom;
    private string? value;

    public async Task<string?> GetAsync(CancellationToken cancellationToken)
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
                var stored = await JsonSerializer.DeserializeAsync<StoredRedirectUri>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
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

    public async Task SaveAsync(string redirectUri, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);

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
                await JsonSerializer.SerializeAsync(stream, new StoredRedirectUri(redirectUri, DateTimeOffset.UtcNow), JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(tempPath, path, overwrite: true);

            loadedFrom = path;
            value = redirectUri;
        }
        finally
        {
            gate.Release();
        }
    }

    private string ResolvePath()
    {
        var path = options.CurrentValue.RedirectUriFilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Zendesk:RedirectUriFilePath is not configured.");
        }

        return Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
    }

    private sealed record StoredRedirectUri(
        [property: JsonPropertyName("redirect_uri")] string RedirectUri,
        [property: JsonPropertyName("registered_at")] DateTimeOffset RegisteredAt);
}
