# Revolut Business

Connects INTERA to Revolut Business via OAuth 2.0 with RSA JWT client assertions (relayed automatically through INTERA Core's public `oauth-callback` webhook endpoint, with AES-256-GCM encrypted token persistence under `/app/App_Data`), exposing accounts, balances, exchange rates, and leg-grain transactions through a companion .NET 11 service.

| | |
| --- | --- |
| Catalog id | `intera.revolut` |
| Runtime id | `intera-revolut-integration` |
| Version | 3.5.0 |
| Integration Contract | 2 |
| License | Apache-2.0 |
| Deployment | Docker (`port: 8080`, `health_path: /health`, `data_path: /app/App_Data`) |

## Container settings (`deployment.settings`)

| Name | Env | Kind | Required | Description |
| --- | --- | --- | --- | --- |
| `client_id` | `REVOLUT__CLIENTID` | `string` | yes | Revolut OAuth Client ID |
| `private_key_pem` | `REVOLUT__PRIVATEKEYPEM` | `secret` | yes | PEM-encoded RSA private key whose public certificate is uploaded in Revolut Business API settings |
| `client_assertion_issuer` | `REVOLUT__CLIENTASSERTIONISSUER` | `string` | yes | Domain of your Redirect URI (without scheme), as required by Revolut JWT client assertions |
| `api_base_url` | `REVOLUT__APIBASEURL` | `string` | no | Revolut Business API base URL (`https://b2b.revolut.com/api/1.0` by default) |
| `business_portal_base_url` | `REVOLUT__BUSINESSPORTALBASEURL` | `string` | no | Revolut Business portal base URL (`https://business.revolut.com` by default) |
| `oauth_endpoint` | `REVOLUT__OAUTHENDPOINT` | `string` | no | OAuth token endpoint (`https://b2b.revolut.com/api/1.0/auth/token` by default) |
| `redirect_uri` | `REVOLUT__REDIRECTURI` | `string` | no | Optional redirect URI override (leave empty to relay OAuth redirects automatically through INTERA Core's webhook address) |
| `transactions_lookback_days` | `REVOLUT__TRANSACTIONSLOOKBACKDAYS` | `number` | no | Lookback window in days when no lower `created_at` filter is set (`90` by default) |

## Connection configuration (`configSchema`)

Zero manual fields (`configSchema: []`). INTERA Core automatically injects `intera_service_url` and `intera_service_token`, registers the `oauth-callback` webhook URL, and surfaces the single-use sign-in link during **Test connection** when authorization is required.

## Build and test locally

```bash
cd integrations/intera/revolut/bundle
npm ci
npm test
cd -
dotnet build -c Release integrations/intera/revolut/service/Intera.Revolut.Api.csproj
node scripts/validate-manifest.mjs integrations/intera/revolut
```

## License

Apache-2.0, see [LICENSE](../../../LICENSE).
