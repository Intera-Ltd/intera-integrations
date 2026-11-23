using System.Security.Cryptography;
using System.Text;

namespace Intera.Common.Authentication;

/// <summary>
/// Validates inbound <c>X-Access-Token</c>/<c>Bearer</c> values against a fixed secret from configuration.
/// When the configured secret is blank (Marketplace container deployment over the internal installation
/// network), <see cref="RequiresToken"/> is <see langword="false"/>; when set (external deployment or
/// explicit defence-in-depth), every protected endpoint requires a matching token.
/// </summary>
public sealed class StaticAccessTokenStore(Func<string?> tokenAccessor, string optionName) : IAccessTokenStore
{
    private static readonly AccessTokenInfo TokenInfo = new(1);

    public bool RequiresToken => !string.IsNullOrWhiteSpace(tokenAccessor());

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
