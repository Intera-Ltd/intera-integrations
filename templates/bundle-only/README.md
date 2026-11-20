# Your Integration

<!-- One paragraph: what the integration reads, from which system, for whom. -->

| | |
| --- | --- |
| Catalog id | `your-org.your-integration` |
| Runtime id | `your_integration` |
| Integration Contract | 2 |
| License | Apache-2.0 |
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

## License

Apache-2.0, the license of this repository (`license: Apache-2.0` in
`manifest.yaml`). To publish the integration under another license, put its
full text in `LICENSE` next to `manifest.yaml` and set `license` to its SPDX
identifier. See [Licensing](../../CONTRIBUTING.md#licensing).
