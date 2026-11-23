# Xero

Connects INTERA to Xero Accounting via OAuth 2.0 (with automatic rotating refresh-token keep-alive), exposing organisations, chart of accounts, contacts, invoices, bills, credit notes, payments, bank transactions, manual journals, budgets, and monthly/aged financial reports through a companion .NET 10 service.

| | |
| --- | --- |
| Catalog id | `intera.xero` |
| Runtime id | `intera-xero-integration` |
| Version | 3.2.0 |
| Integration Contract | 2 |
| License | Apache-2.0 |
| Deployment | Docker (`port: 8080`, `health_path: /health`) |

## Container settings (`deployment.settings`)

| Name | Env | Kind | Required | Description |
| --- | --- | --- | --- | --- |
| `client_id` | `XERO__CLIENTID` | `string` | yes | Xero developer app Client ID |
| `client_secret` | `XERO__CLIENTSECRET` | `secret` | yes | Xero developer app Client Secret |
| `redirect_uri` | `XERO__REDIRECTURI` | `string` | yes | Registered OAuth callback URL (`https://<host>/oauth/callback`) |
| `tenant_id` | `XERO__TENANTID` | `string` | no | Default Xero organisation tenant ID |
| `scopes` | `XERO__SCOPES` | `string` | no | Optional override for Xero read-only scopes |
| `api_access_token` | `XERO__APIACCESSTOKEN` | `secret` | no | Optional inbound shared secret |

## Connection configuration (`configSchema`)

When deployed via Marketplace, Core injects `intera_service_url` automatically.

| Field | Kind | Required | Description |
| --- | --- | --- | --- |
| `apiBaseUrl` | `string` | no | Optional service URL override |
| `accessToken` | `secret` | no | Required only when `XERO__APIACCESSTOKEN` is configured on the service |
| `tenantId` | `string` | no | Per-connection Xero tenant ID override (`X-Xero-Tenant-Id`) |

## Build and test locally

```bash
cd integrations/intera/xero/bundle
npm ci
npm test
cd -
dotnet build integrations/intera/xero/service/Intera.Xero.Api.csproj
node scripts/validate-manifest.mjs integrations/intera/xero
```

## License

Apache-2.0, see [LICENSE](../../../LICENSE).
