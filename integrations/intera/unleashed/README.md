# Unleashed

Connects INTERA to Unleashed inventory, warehouse, purchasing, and sales operations via HMAC-SHA256 signed requests in a stateless companion .NET 10 service.

| | |
| --- | --- |
| Catalog id | `intera.unleashed` |
| Runtime id | `intera-unleashed-integration` |
| Version | 3.2.0 |
| Integration Contract | 2 |
| License | Apache-2.0 |
| Deployment | Docker (`port: 8080`, `health_path: /health`) |

## Container settings (`deployment.settings`)

| Name | Env | Kind | Required | Description |
| --- | --- | --- | --- | --- |
| `api_id` | `UNLEASHED__APIID` | `string` | yes | Unleashed API ID (`api-auth-id`) |
| `api_key` | `UNLEASHED__APIKEY` | `secret` | yes | Unleashed API Key (HMAC-SHA256 signing secret) |
| `api_base_url` | `UNLEASHED__APIBASEURL` | `string` | no | Unleashed API base URL (default: `https://api.unleashedsoftware.com`) |
| `client_type` | `UNLEASHED__CLIENTTYPE` | `string` | no | `client-type` header value (default: `intera/unleashed`) |
| `page_size` | `UNLEASHED__PAGESIZE` | `number` | no | Records per upstream page (default: `200`) |
| `max_pages` | `UNLEASHED__MAXPAGES` | `number` | no | Maximum upstream pages fetched per query (default: `20`) |

## Connection configuration (`configSchema`)

`configSchema` is empty (`[]`). INTERA Core automatically injects `intera_service_url` and `intera_service_token` (`INTERA_SERVICE_TOKEN`) when deploying the container.

## Data sources

- `unleashed_warehouses` — Warehouses
- `unleashed_products` — Products
- `unleashed_stock_on_hand` — Stock On Hand
- `unleashed_purchase_order_lines` — Purchase Order Lines
- `unleashed_sales_order_lines` — Sales Order Lines
- `unleashed_sales_shipment_lines` — Sales Shipment Lines
- `unleashed_stock_transfer_lines` — Warehouse Transfer Lines
- `unleashed_stock_count_lines` — Stock Count Lines
- `unleashed_supplier_return_lines` — Supplier Return Lines

## Build and test locally

```bash
cd integrations/intera/unleashed/bundle
npm ci
npm test
cd -
dotnet build -c Release integrations/intera/unleashed/service/Intera.Unleashed.Api.csproj
node scripts/validate-manifest.mjs integrations/intera/unleashed
```

## License

Apache-2.0, see [LICENSE](../../../LICENSE).
