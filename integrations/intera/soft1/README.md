# EntersoftOne Soft1

Connects INTERA to EntersoftOne Soft1 ERP via the `s1services` Web Service Connector API in a stateless companion .NET 11 service (with automatic UTF-8 / `windows-1253` Greek encoding detection and session renewal).

| | |
| --- | --- |
| Catalog id | `intera.soft1` |
| Runtime id | `intera-soft1-integration` |
| Version | 3.2.0 |
| Integration Contract | 2 |
| License | Apache-2.0 |
| Deployment | Docker (`port: 8080`, `health_path: /health`) |

## Container settings (`deployment.settings`)

| Name | Env | Kind | Required | Description |
| --- | --- | --- | --- | --- |
| `service_url` | `SOFT1__SERVICEURL` | `string` | yes | Soft1 `s1services` HTTPS endpoint (e.g. `https://mycompany.oncloud.gr/s1services`) |
| `username` | `SOFT1__USERNAME` | `string` | yes | Soft1 web account username |
| `password` | `SOFT1__PASSWORD` | `secret` | yes | Soft1 web account password |
| `app_id` | `SOFT1__APPID` | `string` | yes | Registered Soft1 `appId` |
| `company` | `SOFT1__COMPANY` | `string` | no | Optional company code when multiple companies are permitted |
| `branch` | `SOFT1__BRANCH` | `string` | no | Optional branch code |
| `module` | `SOFT1__MODULE` | `string` | no | Optional module code |
| `ref_id` | `SOFT1__REFID` | `string` | no | Optional reference ID |
| `default_fiscal_years` | `SOFT1__DEFAULTFISCALYEARS` | `number` | no | Default fiscal years read without a date range (default: `2`) |
| `max_fiscal_years` | `SOFT1__MAXFISCALYEARS` | `number` | no | Maximum fiscal years per query (default: `5`) |

## Connection configuration (`configSchema`)

`configSchema` is empty (`[]`). INTERA Core automatically injects `intera_service_url` and `intera_service_token` (`INTERA_SERVICE_TOKEN`) when deploying the container.

## Data sources

51 ERP & accounting data sources (`soft1_*`) covering reference catalogs (`soft1_companies`, `soft1_customers`, `soft1_suppliers`, `soft1_items`, `soft1_warehouses`, `soft1_chart_of_accounts`, etc.), sales and purchase documents/orders/lines, general ledger entries, receivables/payables ageing, cash/bank transactions, stock balances/movements, and project financials.

## Build and test locally

```bash
cd integrations/intera/soft1/bundle
npm ci
npm test
cd -
dotnet build -c Release integrations/intera/soft1/service/Intera.Soft1.Api.csproj
node scripts/validate-manifest.mjs integrations/intera/soft1
```

## License

Apache-2.0, see [LICENSE](../../../LICENSE).
