# SanctionsCheck

Downloads official sanctions lists straight from the publisher once per day and evaluates searches, aggregations, and joins locally inside INTERA Core — no counterparty or entity names leave the installation.

| | |
| --- | --- |
| Catalog id | `intera.sanctions` |
| Runtime id | `intera-sanctions-integration` |
| Version | 1.2.0 |
| Integration Contract | 2 |
| License | Apache-2.0 |
| Deployment | Bundle-only (`deployment.type: none`) |

## Connection configuration (`configSchema`)

| Field | Kind | Required | Description |
| --- | --- | --- | --- |
| `eu_list_url` | `string` | no | Optional override URL for the EU FSF XML export (defaults to the European Commission DG FISMA XML 1.1 endpoint) |

## Data sources

- `eu_sanctions_list` — EU Consolidated List of Financial Sanctions (one row per name variant of a listed individual or enterprise, with normalized names, EU/UN reference numbers, legal act details, birth dates, citizenships, addresses, and IMO/identification numbers).

## Build and test locally

```bash
cd integrations/intera/sanctions/bundle
npm ci
npm test
cd -
node scripts/validate-manifest.mjs integrations/intera/sanctions
```

## License

Apache-2.0, see [LICENSE](../../../LICENSE).
