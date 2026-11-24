using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace Intera.Common.Authentication;

/// <summary>
/// Platform secrets injected by INTERA Core into every Docker service container:
/// <c>INTERA_SERVICE_TOKEN</c> (always required) and <c>INTERA_SERVICE_DATA_KEY</c>
/// (required when the manifest declares <c>deployment.data_path</c>).
/// </summary>
public sealed class ServiceSecretsOptions
{
    public const string ServiceTokenEnvironmentVariable = "INTERA_SERVICE_TOKEN";
    public const string DataKeyEnvironmentVariable = "INTERA_SERVICE_DATA_KEY";
    public const int ExpectedDataKeyBytes = 32;

    public string ServiceToken { get; set; } = string.Empty;

    public string DataKey { get; set; } = string.Empty;

    public bool RequiresDataKey { get; set; }
}

/// <summary>
/// Validated 32-byte AES-256-GCM key used to encrypt persistent state under <c>deployment.data_path</c>.
/// </summary>
public sealed class ServiceDataKey
{
    private readonly byte[] keyBytes;

    public ServiceDataKey(byte[] keyBytes)
    {
        ArgumentNullException.ThrowIfNull(keyBytes);
        if (keyBytes.Length != ServiceSecretsOptions.ExpectedDataKeyBytes)
        {
            throw new ArgumentException(
                $"{ServiceSecretsOptions.DataKeyEnvironmentVariable} must decode to {ServiceSecretsOptions.ExpectedDataKeyBytes} bytes, got {keyBytes.Length}.",
                nameof(keyBytes));
        }

        this.keyBytes = keyBytes.ToArray();
    }

    public ReadOnlySpan<byte> AsSpan() => keyBytes;

    public byte[] ToArray() => keyBytes.ToArray();

    public static bool TryDecode(string? rawBase64, out byte[] decoded)
    {
        decoded = [];
        if (string.IsNullOrWhiteSpace(rawBase64))
        {
            return false;
        }

        try
        {
            var bytes = Convert.FromBase64String(rawBase64.Trim());
            if (bytes.Length != ServiceSecretsOptions.ExpectedDataKeyBytes)
            {
                CryptographicOperations.ZeroMemory(bytes);
                return false;
            }

            decoded = bytes;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed class ServiceSecretsOptionsValidator : IValidateOptions<ServiceSecretsOptions>
{
    public ValidateOptionsResult Validate(string? name, ServiceSecretsOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ServiceToken))
        {
            failures.Add(
                $"{ServiceSecretsOptions.ServiceTokenEnvironmentVariable} is required and was not provided.");
        }

        if (options.RequiresDataKey)
        {
            if (!ServiceDataKey.TryDecode(options.DataKey, out var bytes))
            {
                failures.Add(
                    $"{ServiceSecretsOptions.DataKeyEnvironmentVariable} is required for stateful services (base64-encoded 32-byte key).");
            }
            else
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
