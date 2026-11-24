using Intera.Common.Authentication;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Intera.Common.Infrastructure.OAuth;

/// <summary>
/// Persists an integration's OAuth token to an AES-256-GCM encrypted file under <c>deployment.data_path</c>
/// (<c>INTERA_SERVICE_DATA_KEY</c>), writing atomically through a temporary file and rename.
/// Also accepts legacy unencrypted JSON files so existing volumes upgrade seamlessly.
/// </summary>
public sealed class FileOAuthTokenStore : IOAuthTokenStore
{
    private const int EnvelopeVersion = 1;
    private const string EnvelopeAlgorithm = "A256GCM";
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Func<string> pathProvider;
    private readonly ServiceDataKey dataKey;
    private readonly SemaphoreSlim gate = new(1, 1);

    public FileOAuthTokenStore(Func<string> pathProvider, ServiceDataKey dataKey)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        ArgumentNullException.ThrowIfNull(dataKey);
        this.pathProvider = pathProvider;
        this.dataKey = dataKey;
    }

    public FileOAuthTokenStore(Func<string> pathProvider, byte[] dataKey)
        : this(pathProvider, new ServiceDataKey(dataKey))
    {
    }

    public async Task<OAuthToken?> GetAsync(string provider, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var rawBytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            if (rawBytes.Length == 0)
            {
                return null;
            }

            var token = DeserializeStoredToken(rawBytes);

            return token is not null && string.Equals(token.Provider, provider, StringComparison.Ordinal)
                ? token
                : null;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(OAuthToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        var path = ResolvePath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(
            token with { UpdatedAt = DateTimeOffset.UtcNow },
            JsonOptions);

        byte[] envelopeBytes;
        try
        {
            var nonce = new byte[NonceSizeBytes];
            RandomNumberGenerator.Fill(nonce);

            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagSizeBytes];

            using (var aes = new AesGcm(dataKey.AsSpan(), TagSizeBytes))
            {
                aes.Encrypt(nonce, plaintext, ciphertext, tag);
            }

            var envelope = new EncryptedTokenEnvelope(
                EnvelopeVersion,
                EnvelopeAlgorithm,
                Convert.ToBase64String(nonce),
                Convert.ToBase64String(tag),
                Convert.ToBase64String(ciphertext));

            envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tempPath = $"{path}.tmp";
            await File.WriteAllBytesAsync(tempPath, envelopeBytes, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            gate.Release();
        }
    }

    private OAuthToken? DeserializeStoredToken(byte[] rawBytes)
    {
        using var document = JsonDocument.Parse(rawBytes);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("v", out var versionProp) &&
            versionProp.ValueKind == JsonValueKind.Number)
        {
            var version = versionProp.GetInt32();
            if (version != EnvelopeVersion)
            {
                throw new InvalidOperationException($"Unsupported encrypted token file format version: {version}.");
            }

            var envelope = JsonSerializer.Deserialize<EncryptedTokenEnvelope>(rawBytes, JsonOptions)
                ?? throw new InvalidOperationException("Encrypted OAuth token envelope could not be parsed.");

            if (!string.Equals(envelope.Algorithm, EnvelopeAlgorithm, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unsupported token encryption algorithm: {envelope.Algorithm}.");
            }

            var nonce = Convert.FromBase64String(envelope.Nonce);
            var tag = Convert.FromBase64String(envelope.Tag);
            var ciphertext = Convert.FromBase64String(envelope.Ciphertext);

            if (nonce.Length != NonceSizeBytes || tag.Length != TagSizeBytes)
            {
                throw new CryptographicException("Encrypted OAuth token envelope has invalid nonce or tag length.");
            }

            var plaintext = new byte[ciphertext.Length];
            try
            {
                using var aes = new AesGcm(dataKey.AsSpan(), TagSizeBytes);
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
                return JsonSerializer.Deserialize<OAuthToken>(plaintext, JsonOptions);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        return JsonSerializer.Deserialize<OAuthToken>(rawBytes, JsonOptions);
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

    private sealed record EncryptedTokenEnvelope(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("alg")] string Algorithm,
        [property: JsonPropertyName("nonce")] string Nonce,
        [property: JsonPropertyName("tag")] string Tag,
        [property: JsonPropertyName("ciphertext")] string Ciphertext);
}
