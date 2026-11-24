# Zoho Inventory

Connects INTERA to a Zoho Inventory organization via a Zoho Self Client refresh token, exposing customers, vendors, items, stock by item/location, sales orders, purchase orders, receipts, packages, shipments, invoices, and bills (with dynamic custom-field provisioning) through a stateless companion .NET 10 service.

| | |
| --- | --- |
| Catalog id | `intera.zoho-inventory` |
| Runtime id | `intera-zoho-inventory-integration` |
| Version | 3.4.0 |
| INTERA Core | `>=0.5.94` |
| Integration Contract | 2 |
| License | Apache-2.0 |
| Deployment | Docker (`port: 8080`, `health_path: /health`) |

## Container settings (`deployment.settings`)

INTERA Core automatically provisions `INTERA_SERVICE_TOKEN` for service-to-bundle authentication.

| Name | Env | Kind | Required | Description |
| --- | --- | --- | --- | --- |
| `data_center` | `ZOHOINVENTORY__DATACENTER` | `string` | no | Zoho region (`com`, `eu`, `in`, `com.au`, `jp`, `ca`, `com.cn`, `sa`; default: `com`) |
| `organization_id` | `ZOHOINVENTORY__ORGANIZATIONID` | `string` | yes | Zoho Inventory organization ID |
| `client_id` | `ZOHOINVENTORY__CLIENTID` | `string` | yes | Zoho API Console Self Client ID |
| `client_secret` | `ZOHOINVENTORY__CLIENTSECRET` | `secret` | yes | Zoho API Console Self Client Secret |
| `refresh_token` | `ZOHOINVENTORY__REFRESHTOKEN` | `secret` | yes | Refresh token minted from Self Client grant code |
| `accounts_url` | `ZOHOINVENTORY__ACCOUNTSURL` | `string` | no | Optional Zoho Accounts host override |
| `api_base_url` | `ZOHOINVENTORY__APIBASEURL` | `string` | no | Optional Zoho Inventory API root override |

## Connection configuration (`configSchema`)

None (`configSchema: []`). When deployed via Marketplace, Core injects `intera_service_url` and `intera_service_token` automatically.

## Data sources

- `zoho_inventory_customers`, `zoho_inventory_vendors`, `zoho_inventory_contacts`, `zoho_inventory_salespersons`
- `zoho_inventory_items`, `zoho_inventory_locations`, `zoho_inventory_inventory_by_item`, `zoho_inventory_inventory_by_location`
- `zoho_inventory_sales_orders`, `zoho_inventory_sales_order_lines`
- `zoho_inventory_purchase_orders`, `zoho_inventory_purchase_order_lines`
- `zoho_inventory_purchase_receives`, `zoho_inventory_purchase_receive_lines`
- `zoho_inventory_packages`, `zoho_inventory_package_lines`
- `zoho_inventory_shipments`, `zoho_inventory_shipment_lines`
- `zoho_inventory_invoices`, `zoho_inventory_invoice_lines`
- `zoho_inventory_bills`, `zoho_inventory_bill_lines`

## Build and test locally

```bash
cd integrations/intera/zoho-inventory/bundle
npm ci
npm test
cd -
dotnet build -c Release integrations/intera/zoho-inventory/service/Intera.ZohoInventory.Api.csproj
node scripts/validate-manifest.mjs integrations/intera/zoho-inventory
```

## License

Apache-2.0, see [LICENSE](../../../LICENSE).
