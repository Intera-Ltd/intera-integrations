using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Intera.Common.Infrastructure.OAuth;

public sealed class RsaJwtClientAssertionFactory : IClientAssertionFactory
{
    public string CreateClientAssertion(string clientId, string issuer, string audience, string privateKeyPath)
    {
        if (string.IsNullOrWhiteSpace(privateKeyPath) || !File.Exists(privateKeyPath))
        {
            throw new FileNotFoundException("Private key file was not found.", privateKeyPath);
        }

        var privateKeyPem = File.ReadAllText(privateKeyPath);
        return CreateClientAssertionFromPem(clientId, issuer, audience, privateKeyPem);
    }

    public string CreateClientAssertionFromPem(string clientId, string issuer, string audience, string privateKeyPem)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("OAuth client id is not configured.");
        }

        if (string.IsNullOrWhiteSpace(issuer))
        {
            throw new InvalidOperationException("OAuth client assertion issuer is not configured.");
        }

        if (string.IsNullOrWhiteSpace(audience))
        {
            throw new InvalidOperationException("OAuth client assertion audience is not configured.");
        }

        if (string.IsNullOrWhiteSpace(privateKeyPem))
        {
            throw new InvalidOperationException("OAuth RSA private key PEM is not configured.");
        }

        using var rsa = RSA.Create();
        var normalizedPem = privateKeyPem.Replace("\\n", "\n", StringComparison.Ordinal).Trim();
        rsa.ImportFromPem(normalizedPem);

        var now = DateTimeOffset.UtcNow;
        var credentials = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256)
        {
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, clientId),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
            ],
            notBefore: now.UtcDateTime,
            expires: now.AddMinutes(5).UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
