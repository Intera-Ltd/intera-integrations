# Your Integration

<!-- One paragraph: what the integration reads, from which system, for whom. -->

| | |
| --- | --- |
| Catalog id | `your-org.your-integration` |
| Runtime id | `your_integration` |
| Integration Contract | 2 |
| License | Apache-2.0 |
| Deployment | Docker, port 8080, health path `/health` |
| Data sources | `records` |

## Configuration

No connection fields: Core passes the container address to the bundle as `intera_service_url`.

Service API:

| Method and path | Answer |
| --- | --- |
| `GET /health` | `200` once the service is ready |
| `GET /api/records` | JSON list of records |

## Build and test

```bash
cd bundle && npm ci && npm test && cd ..
docker build -t your-integration:dev service
```

## Data

<!-- Describe each data source: fields, identity key, units, refresh. -->

## License

Apache-2.0, the license of this repository (`license: Apache-2.0` in
`manifest.yaml`). To publish the integration under another license, put its
full text in `LICENSE` next to `manifest.yaml` and set `license` to its SPDX
identifier. See [Licensing](../../CONTRIBUTING.md#licensing).
