# Your Integration

<!-- One paragraph: what the integration reads, from which system, for whom. -->

| | |
| --- | --- |
| Catalog id | `your-org.your-integration` |
| Runtime id | `your_integration` |
| Integration Contract | 2 |
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
