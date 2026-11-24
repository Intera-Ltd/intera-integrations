# Salesforce

Connects INTERA to Salesforce CRM via an OAuth 2.0 client-credentials Connected App / External Client App, compiling filters, ordering, and paging into SOQL in a stateless companion .NET 11 service.

| | |
| --- | --- |
| Catalog id | `intera.salesforce` |
| Runtime id | `intera-salesforce-integration` |
| Version | 3.3.0 |
| INTERA Core | `>=0.5.94` |
| Integration Contract | 2 |
| License | Apache-2.0 |
| Deployment | Docker (`port: 8080`, `health_path: /health`) |

## Container settings (`deployment.settings`)

INTERA Core automatically provisions `INTERA_SERVICE_TOKEN` for service-to-bundle authentication.

| Name | Env | Kind | Required | Description |
| --- | --- | --- | --- | --- |
| `login_url` | `SALESFORCE__LOGINURL` | `string` | yes | Salesforce My Domain URL (`https://acme.my.salesforce.com`) |
| `client_id` | `SALESFORCE__CLIENTID` | `string` | yes | Connected App Consumer Key |
| `client_secret` | `SALESFORCE__CLIENTSECRET` | `secret` | yes | Connected App Consumer Secret |
| `instance_url` | `SALESFORCE__INSTANCEURL` | `string` | no | Optional Salesforce instance host override |
| `api_version` | `SALESFORCE__APIVERSION` | `string` | no | REST API version (default: `v67.0`) |

## Connection configuration (`configSchema`)

None (`configSchema: []`). When deployed via Marketplace, Core injects `intera_service_url` and `intera_service_token` automatically.

## Data sources

- `salesforce_accounts` — Accounts
- `salesforce_opportunities` — Opportunities
- `salesforce_cases` — Support cases
- `salesforce_tasks` — Tasks
- `salesforce_events` — Calendar events
- `salesforce_orders` — Customer orders

## Build and test locally

```bash
cd integrations/intera/salesforce/bundle
npm ci
npm test
cd -
dotnet build -c Release integrations/intera/salesforce/service/Intera.Salesforce.Api.csproj
node scripts/validate-manifest.mjs integrations/intera/salesforce
```

## License

Apache-2.0, see [LICENSE](../../../LICENSE).
