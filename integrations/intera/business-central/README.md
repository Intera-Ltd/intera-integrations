# Microsoft Dynamics 365 Business Central

Connects INTERA to Microsoft Dynamics 365 Business Central via Microsoft Entra ID OAuth 2.0 client-credentials authentication and the Business Central v2.0 OData API in a stateless companion .NET 11 service.

| | |
| --- | --- |
| Catalog id | `intera.business-central` |
| Runtime id | `intera-businesscentral-integration` |
| Version | 3.2.0 |
| Integration Contract | 2 |
| License | Apache-2.0 |
| Deployment | Docker (`port: 8080`, `health_path: /health`) |

## Container settings (`deployment.settings`)

| Name | Env | Kind | Required | Description |
| --- | --- | --- | --- | --- |
| `tenant_id` | `BUSINESSCENTRAL__TENANTID` | `string` | yes | Microsoft Entra ID tenant GUID |
| `environment` | `BUSINESSCENTRAL__ENVIRONMENT` | `string` | no | Business Central environment name (default: `Production`) |
| `client_id` | `BUSINESSCENTRAL__CLIENTID` | `string` | yes | Microsoft Entra ID app registration Client ID |
| `client_secret` | `BUSINESSCENTRAL__CLIENTSECRET` | `secret` | yes | Microsoft Entra ID app registration Client Secret |
| `api_base_url` | `BUSINESSCENTRAL__APIBASEURL` | `string` | no | Optional Business Central v2.0 API base URL override |
| `oauth_endpoint` | `BUSINESSCENTRAL__OAUTHENDPOINT` | `string` | no | Optional Microsoft Entra ID token endpoint override |
| `max_records_per_entity` | `BUSINESSCENTRAL__MAXRECORDSPERENTITY` | `number` | no | Maximum OData records fetched per entity read (default: `5000`) |

## Connection configuration (`configSchema`)

`configSchema` is empty (`[]`). INTERA Core automatically injects `intera_service_url` and `intera_service_token` (`INTERA_SERVICE_TOKEN`) when deploying the container.

## Data sources

- `bc-companies` — Business Central Companies
- `bc-customers` — Business Central Customers (scoped by `company_id = "<guid>"`)
- `bc-vendors` — Business Central Vendors (scoped by `company_id = "<guid>"`)
- `bc-sales-invoices` — Business Central Sales Invoices (scoped by `company_id = "<guid>"`)
- `bc-purchase-invoices` — Business Central Purchase Invoices (scoped by `company_id = "<guid>"`)
- `bc-gl-entries` — Business Central General Ledger Entries (scoped by `company_id = "<guid>"`)

## Build and test locally

```bash
cd integrations/intera/business-central/bundle
npm ci
npm test
cd -
dotnet build -c Release integrations/intera/business-central/service/Intera.BusinessCentral.Api.csproj
node scripts/validate-manifest.mjs integrations/intera/business-central
```

## License

Apache-2.0, see [LICENSE](../../../LICENSE).
