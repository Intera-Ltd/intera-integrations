# Jira

Connects INTERA to Jira Cloud via Atlassian OAuth 2.0 (3LO, relayed automatically through INTERA Core's public `oauth-callback` webhook endpoint) with rotating refresh-token keep-alive, JQL pushdown, and custom-field discovery through a companion .NET 10 service.

| | |
| --- | --- |
| Catalog id | `intera.jira` |
| Runtime id | `intera-jira-integration` |
| Version | 3.3.0 |
| Integration Contract | 2 |
| License | Apache-2.0 |
| Deployment | Docker (`port: 8080`, `health_path: /health`) |

## Container settings (`deployment.settings`)

| Name | Env | Kind | Required | Description |
| --- | --- | --- | --- | --- |
| `client_id` | `JIRA__CLIENTID` | `string` | yes | Atlassian 3LO app Client ID |
| `client_secret` | `JIRA__CLIENTSECRET` | `secret` | yes | Atlassian 3LO app Client Secret |
| `redirect_uri` | `JIRA__REDIRECTURI` | `string` | no | Optional callback URL override (empty relays OAuth redirects through INTERA Core's webhook address) |
| `site_url` | `JIRA__SITEURL` | `string` | no | Default Jira site URL or Cloud ID |
| `jql_scope` | `JIRA__JQLSCOPE` | `string` | no | Deployment-wide JQL restriction |
| `issue_custom_fields` | `JIRA__ISSUECUSTOMFIELDS` | `string` | no | Custom fields included on `jira_issues` (`*` or comma-separated IDs/names) |
| `scopes` | `JIRA__SCOPES` | `string` | no | Optional override for read-only Atlassian scopes |
| `api_access_token` | `JIRA__APIACCESSTOKEN` | `secret` | no | Optional inbound shared secret |

## Connection configuration (`configSchema`)

When deployed via Marketplace, Core injects `intera_service_url` automatically.

| Field | Kind | Required | Description |
| --- | --- | --- | --- |
| `apiBaseUrl` | `string` | no | Optional service URL override |
| `accessToken` | `secret` | no | Required only when `JIRA__APIACCESSTOKEN` is configured on the service |
| `siteUrl` | `string` | no | Per-connection Jira site URL / Cloud ID (`X-Jira-Site`) |
| `jqlScope` | `string` | no | Per-connection narrowing JQL (`X-Jira-Jql-Scope`) |

## Build and test locally

```bash
cd integrations/intera/jira/bundle
npm ci
npm test
cd -
dotnet build integrations/intera/jira/service/Intera.Jira.Api.csproj
node scripts/validate-manifest.mjs integrations/intera/jira
```

## License

Apache-2.0, see [LICENSE](../../../LICENSE).
