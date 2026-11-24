namespace Intera.Common.Infrastructure.OAuth;

public interface IClientAssertionFactory
{
    string CreateClientAssertion(string clientId, string issuer, string audience, string privateKeyPath);

    string CreateClientAssertionFromPem(string clientId, string issuer, string audience, string privateKeyPem);
}
