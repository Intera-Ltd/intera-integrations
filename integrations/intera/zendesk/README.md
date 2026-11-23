# Zendesk

Connects INTERA to Zendesk Support via OAuth 2.0 (relayed automatically through INTERA Core's public `oauth-callback` webhook endpoint so the companion .NET 10 service requires no public ingress of its own), exposing tickets, ticket metrics, SLA events, comments, satisfaction ratings, groups, users, organizations, forms, and SLA policies.

| | |
| --- | --- |
| Catalog id | `intera.zendesk` |
| Runtime id | `intera-zendesk-integration` |
| Version | 4.1.1 |
| Integration Contract | 2 |
| License | Apache-2.0 |
| Deployment | Docker (`port: 8080`, `health_path: /health`) |

## Container settings (`deployment.settings`)

| Name | Env | Kind | Required | Description |
| --- | --- | --- | --- | --- |
| `subdomain` | `ZENDESK__SUBDOMAIN` | `string` | yes | Zendesk subdomain (`acme` for `https://acme.zendesk.com`) |
| `client_id` | `ZENDESK__CLIENTID` | `string` | yes | Zendesk OAuth client unique identifier |
| `client_secret` | `ZENDESK__CLIENTSECRET` | `secret` | yes | Zendesk OAuth client secret |
| `redirect_uri` | `ZENDESK__REDIRECTURI` | `string` | no | Optional redirect URL override (empty uses INTERA's relayed webhook callback) |
| `base_url` | `ZENDESK__BASEURL` | `string` | no | Optional Zendesk base URL override |
| `ticket_custom_fields` | `ZENDESK__TICKETCUSTOMFIELDS` | `string` | no | Custom fields included on `zendesk_tickets` (`*` or comma-separated IDs/titles) |
| `use_incremental_export` | `ZENDESK__USEINCREMENTALEXPORT` | `boolean` | no | Use cursor-based Incremental Export API (default: `false`) |
| `scopes` | `ZENDESK__SCOPES` | `string` | no | OAuth scopes (default: `read`) |
| `api_access_token` | `ZENDESK__APIACCESSTOKEN` | `secret` | no | Optional inbound shared secret |

## Connection configuration (`configSchema`)

When deployed via Marketplace, Core injects `intera_service_url` automatically and both fields below can be left blank.

| Field | Kind | Required | Description |
| --- | --- | --- | --- |
| `apiBaseUrl` | `string` | no | Optional service URL override |
| `accessToken` | `secret` | no | Required only when `ZENDESK__APIACCESSTOKEN` is configured on the service |

## Build and test locally

```bash
cd integrations/intera/zendesk/bundle
npm ci
npm test
cd -
dotnet build integrations/intera/zendesk/service/Intera.Zendesk.Api.csproj
node scripts/validate-manifest.mjs integrations/intera/zendesk
```

## License

Apache-2.0, see [LICENSE](../../../LICENSE).
