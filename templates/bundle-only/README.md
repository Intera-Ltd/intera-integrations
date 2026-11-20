# Your Integration

<!-- One paragraph: what the integration reads, from which system, for whom. -->

| | |
| --- | --- |
| Catalog id | `your-org.your-integration` |
| Runtime id | `your_integration` |
| Integration Contract | 2 |
| Deployment | none (bundle only) |
| Data sources | `records` |

## Configuration

| Field | Kind | Description |
| --- | --- | --- |
| `base_url` | string | Base URL of the external API |
| `api_key` | secret | API key, stored encrypted by Core |

## Build and test

```bash
cd bundle && npm ci && npm test && cd ..

```

## Data

<!-- Describe each data source: fields, identity key, units, refresh. -->
