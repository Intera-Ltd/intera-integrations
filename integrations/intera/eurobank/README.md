# Eurobank

Connects INTERA to Eurobank Cyprus B2B / Open Banking v2 via OAuth 2.0 Authorization Code flow with optional mTLS client certificates (relayed automatically through INTERA Core's public `oauth-callback` webhook endpoint, with AES-256-GCM encrypted token persistence under `/app/App_Data`), exposing bank accounts and balances through a companion .NET 11 service.

| | |
| --- | --- |
| Catalog id | `intera.eurobank` |
| Runtime id | `intera-eurobank-integration` |
| Version | 3.2.0 |
| Integration Contract | 2 |
| License | Apache-2.0 |
| Deployment | Docker (`port: 8080`, `health_path: /health`, `data_path: /app/App_Data`) |

## Container settings (`deployment.settings`)

| Name | Env | Kind | Required | Description |
| --- | --- | --- | --- | --- |
| `client_id` | `EUROBANK__CLIENTID` | `string` | yes | Eurobank Developer Portal application Client ID |
| `client_secret` | `EUROBANK__CLIENTSECRET` | `secret` | yes | Eurobank Developer Portal application Client Secret |
| `api_base_url` | `EUROBANK__APIBASEURL` | `string` | no | Resource API base URL (`https://apis.hellenicbank.com/v2/b2b` by default) |
| `auth_base_url` | `EUROBANK__AUTHBASEURL` | `string` | no | OAuth 2.0 server base URL (`https://oauth.hellenicbank.com` by default) |
| `scope` | `EUROBANK__SCOPE` | `string` | no | Comma-separated Eurobank v2 OAuth scopes |
| `redirect_uri` | `EUROBANK__REDIRECTURI` | `string` | no | Optional redirect URI override (leave empty to relay OAuth redirects automatically through INTERA Core's webhook address) |
| `client_certificate_pem` | `EUROBANK__CLIENTCERTIFICATEPEM` | `secret` | no | PEM-encoded mTLS client certificate issued by the bank (mandatory in production, optional in sandbox) |
| `client_private_key_pem` | `EUROBANK__CLIENTPRIVATEKEYPEM` | `secret` | no | PEM-encoded private key matching `client_certificate_pem` |
| `client_certificate_pfx_base64` | `EUROBANK__CLIENTCERTIFICATEPFXBASE64` | `secret` | no | Alternative compact Base64-encoded PKCS#12 (`.pfx`) bundle when under 4096 characters |
| `client_certificate_password` | `EUROBANK__CLIENTCERTIFICATEPASSWORD` | `secret` | no | Password for encrypted PKCS#12 bundle or private key |

## Connection configuration (`configSchema`)

Zero manual fields (`configSchema: []`). INTERA Core automatically injects `intera_service_url` and `intera_service_token`, registers the `oauth-callback` webhook URL, and surfaces the single-use sign-in link during **Test connection** when authorization is required.

## Build and test locally

```bash
cd integrations/intera/eurobank/bundle
npm ci
npm test
cd -
dotnet build -c Release integrations/intera/eurobank/service/Intera.Eurobank.Api.csproj
node scripts/validate-manifest.mjs integrations/intera/eurobank
```

## License

Apache-2.0, see [LICENSE](../../../LICENSE).
