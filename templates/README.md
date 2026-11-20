# Templates

Starting points for a new integration. Copy one into
`integrations/<developer>/<integration>/` and replace every `your-org`,
`your-integration` and `your_integration`.

| Template | When to use |
| --- | --- |
| [`bundle-only`](bundle-only/) | The bundle talks to an external API directly. Core loads only the bundle; the administrator enters the API address and key in the connection settings. |
| [`bundle-with-service`](bundle-with-service/) | The logic lives in your own service (any language) that runs in a Docker container next to INTERA. The bundle reads `intera_service_url` and calls the service over HTTP. |

See [docs/getting-started.md](../docs/getting-started.md) for the walkthrough.
