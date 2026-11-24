using Intera.Eurobank.Api.Options;
using Microsoft.Extensions.Options;
using System.Security.Cryptography.X509Certificates;

namespace Intera.Eurobank.Api.Infrastructure;

/// <summary>
/// Primary HTTP handler for Eurobank resource calls, attaching the bank-issued mTLS client certificate from
/// container secrets (PEM pair or Base64 PKCS#12) or an optional local <c>.pfx</c> file. In sandbox environments
/// mTLS is optional, so startup proceeds cleanly when no client certificate is configured.
/// </summary>
public sealed class EurobankClientCertificateHandler : HttpClientHandler
{
    public EurobankClientCertificateHandler(IOptionsMonitor<EurobankOptions> options)
    {
        var current = options.CurrentValue;
        var certificate = TryLoadCertificate(current);
        if (certificate is null)
        {
            return;
        }

        ClientCertificates.Add(certificate);
        ClientCertificateOptions = ClientCertificateOption.Manual;
    }

    private static X509Certificate2? TryLoadCertificate(EurobankOptions options)
    {
        var password = string.IsNullOrEmpty(options.ClientCertificatePassword)
            ? null
            : options.ClientCertificatePassword;

        if (!string.IsNullOrWhiteSpace(options.ClientCertificatePem) &&
            !string.IsNullOrWhiteSpace(options.ClientPrivateKeyPem))
        {
            var certPem = NormalizePem(options.ClientCertificatePem);
            var keyPem = NormalizePem(options.ClientPrivateKeyPem);

            using var ephemeral = password is not null && keyPem.Contains("ENCRYPTED", StringComparison.OrdinalIgnoreCase)
                ? X509Certificate2.CreateFromEncryptedPem(certPem, keyPem, password)
                : X509Certificate2.CreateFromPem(certPem, keyPem);

            return X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pkcs12), null);
        }

        if (!string.IsNullOrWhiteSpace(options.ClientCertificatePfxBase64))
        {
            var pfxBytes = Convert.FromBase64String(options.ClientCertificatePfxBase64.Trim());
            return X509CertificateLoader.LoadPkcs12(pfxBytes, password);
        }

        var certificatePath = ResolveCertificatePath(options.ClientCertificatePath);
        if (!string.IsNullOrWhiteSpace(certificatePath) && File.Exists(certificatePath))
        {
            return X509CertificateLoader.LoadPkcs12FromFile(certificatePath, password);
        }

        return null;
    }

    private static string NormalizePem(string pem) =>
        pem.Replace("\\n", "\n", StringComparison.Ordinal).Trim();

    private static string ResolveCertificatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
    }
}
