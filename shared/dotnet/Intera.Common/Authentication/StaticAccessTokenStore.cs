using System.Security.Cryptography;
using System.Text;

namespace Intera.Common.Authentication;

/// <summary>
/// Validates inbound <c>Authorization: Bearer &lt;token&gt;</c> values in constant time against
/// the platform token issued by INTERA Core (<c>INTERA_SERVICE_TOKEN</c>).
/// </summary>
public sealed class StaticAccessTokenStore(Func<string?> tokenAccessor, string optionName) : IAccessTokenStore
{
    private static readonly AccessTokenInfo TokenInfo = new(1);

    public Task<AccessTokenInfo?> TryValidateAsync(string token, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var configured = tokenAccessor();
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException($"{optionName} is not configured.");
        }

        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(token),
            Encoding.UTF8.GetBytes(configured));

        return Task.FromResult(matches ? TokenInfo : null);
    }
}
