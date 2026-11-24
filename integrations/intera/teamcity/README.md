# JetBrains TeamCity

Connects INTERA to JetBrains TeamCity via personal access token authentication (`Authorization: Bearer`) in a stateless companion .NET 11 service to monitor build configurations, running/queued builds, and latest test results.

| | |
| --- | --- |
| Catalog id | `intera.teamcity` |
| Runtime id | `intera-teamcity-integration` |
| Version | 3.2.0 |
| Integration Contract | 2 |
| License | Apache-2.0 |
| Deployment | Docker (`port: 8080`, `health_path: /health`) |

## Container settings (`deployment.settings`)

| Name | Env | Kind | Required | Description |
| --- | --- | --- | --- | --- |
| `server_url` | `TEAMCITY__SERVERURL` | `string` | yes | TeamCity server HTTPS URL (e.g. `https://teamcity.example.com`) |
| `api_token` | `TEAMCITY__TOKEN` | `secret` | yes | TeamCity personal access token (`Project viewer` role recommended) |
| `test_build_lookback` | `TEAMCITY__TESTBUILDLOOKBACK` | `number` | no | Recent finished builds checked per configuration for reported tests (default: `5`) |
| `max_build_types` | `TEAMCITY__MAXBUILDTYPES` | `number` | no | Maximum build configurations read in one test-results query (default: `100`) |
| `max_tests_per_build` | `TEAMCITY__MAXTESTSPERBUILD` | `number` | no | Maximum test occurrences fetched per build (default: `10000`) |

## Connection configuration (`configSchema`)

`configSchema` is empty (`[]`). INTERA Core automatically injects `intera_service_url` and `intera_service_token` (`INTERA_SERVICE_TOKEN`) when deploying the container.

## Data sources

- `teamcity_build_types` — TeamCity build configurations (latest finished build status, test counters, running and queued counts)
- `teamcity_test_results` — TeamCity test results (latest finished build that reported tests per configuration)

## Build and test locally

```bash
cd integrations/intera/teamcity/bundle
npm ci
npm test
cd -
dotnet build -c Release integrations/intera/teamcity/service/Intera.TeamCity.Api.csproj
node scripts/validate-manifest.mjs integrations/intera/teamcity
```

## License

Apache-2.0, see [LICENSE](../../../LICENSE).
