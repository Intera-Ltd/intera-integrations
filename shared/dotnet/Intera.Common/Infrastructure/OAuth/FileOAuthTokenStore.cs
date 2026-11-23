using System.Text.Json;

namespace Intera.Common.Infrastructure.OAuth;

/// <summary>
/// Persists a project's single OAuth token to a JSON file on disk, so a project holding a refresh token needs no
/// database of its own. Register as a singleton, so the write lock is actually shared across requests.
/// </summary>
/// <remarks>
/// The file path is read through a delegate, the way <c>StaticAccessTokenStore</c> reads its
/// secret. It is re-read on every call, and a relative path resolves against the application's base directory.
/// </remarks>
public sealed class FileOAuthTokenStore(Func<string> pathProvider) : IOAuthTokenStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<OAuthToken?> GetAsync(string provider, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath();

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            await using var stream = File.OpenRead(path);
            var token = await JsonSerializer.DeserializeAsync<OAuthToken>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);

            return token is not null && string.Equals(token.Provider, provider, StringComparison.Ordinal)
                ? token
                : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(OAuthToken token, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tempPath = $"{path}.tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, token with { UpdatedAt = DateTimeOffset.UtcNow }, JsonOptions, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }

    private string ResolvePath()
    {
        var path = pathProvider();
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("The OAuth token file path is not configured.");
        }

        return Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
    }
}
