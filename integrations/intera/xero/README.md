# Xero

Connects INTERA to Xero Accounting via OAuth 2.0 (relayed automatically through INTERA Core's public `oauth-callback` webhook endpoint, with AES-256-GCM encrypted persistent token storage and automatic rotating refresh-token keep-alive), exposing organisations, chart of accounts, contacts, invoices, bills, credit notes, payments, bank transactions, manual journals, budgets, and monthly/aged financial reports through a companion .NET 10 service.

| | |
| --- | --- |
| Catalog id | `intera.xero` |
| Runtime id | `intera-xero-integration` |
| Version | 3.4.0 |
| INTERA Core | `>=0.5.94` |
| Integration Contract | 2 |
| License | Apache-2.0 |
| Deployment | Docker (`port: 8080`, `health_path: /health`, `data_path: /app/App_Data`) |

## Container settings (`deployment.settings`)

INTERA Core automatically provisions `INTERA_SERVICE_TOKEN` and `INTERA_SERVICE_DATA_KEY` (plus a persistent volume mounted at `/app/App_Data`).

| Name | Env | Kind | Required | Description |
| --- | --- | --- | --- | --- |
| `client_id` | `XERO__CLIENTID` | `string` | yes | Xero developer app Client ID |
| `client_secret` | `XERO__CLIENTSECRET` | `secret` | yes | Xero developer app Client Secret |
| `redirect_uri` | `XERO__REDIRECTURI` | `string` | no | Optional redirect URI override (empty relays OAuth redirects through INTERA Core's webhook address) |
| `tenant_id` | `XERO__TENANTID` | `string` | no | Default Xero organisation tenant ID |
| `scopes` | `XERO__SCOPES` | `string` | no | Optional override for Xero read-only scopes |

## Connection configuration (`configSchema`)

When deployed via Marketplace, Core injects `intera_service_url` and `intera_service_token` automatically.

| Field | Kind | Required | Description |
| --- | --- | --- | --- |
| `tenantId` | `string` | no | Per-connection Xero tenant ID override (`X-Xero-Tenant-Id`) |

## Build and test locally

```bash
cd integrations/intera/xero/bundle
npm ci
npm test
cd -
dotnet build -c Release integrations/intera/xero/service/Intera.Xero.Api.csproj
node scripts/validate-manifest.mjs integrations/intera/xero
```

## License

Apache-2.0, see [LICENSE](../../../LICENSE).
