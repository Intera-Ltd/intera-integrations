# Hello .NET

A sample INTERA integration with its own service in a Docker container. Use it
as a reference for integrations whose logic lives in a service written in any
language, with a thin TypeScript bundle in front of it.

| | |
| --- | --- |
| Catalog id | `intera.hello-dotnet` |
| Runtime id | `hello_dotnet` |
| Version | 1.0.0 |
| Integration Contract | 2 |
| License | Apache-2.0 (the repository license) |
| Deployment | Docker, port 8080, health path `/health` |
| Data sources | `items` — "Hello .NET items" |

## How it works

```
INTERA Core ──loads──> bundle (dist/index.js)
                           │  GET {intera_service_url}/api/items
                           ▼
                     hello-dotnet container (.NET 11, port 8080)
```

1. An administrator installs the integration from **Settings → Marketplace**.
2. The installation agent pulls the image pinned in `deployment.image`, starts
   the container in the installation network and waits for `GET /health`.
3. Core loads the bundle and passes the container address to it as
   `intera_service_url` in the connection config.
4. The administrator creates a connection in **Settings → Integrations**.
   "Test connection" calls `/health`; the `items` data source calls
   `/api/items`.

The bundle never hardcodes the address and the service needs no secrets.

## Layout

```
manifest.yaml          catalog manifest (docs/manifest-reference.md)
bundle/
  src/constants.ts     ids, paths, timeouts
  src/types.ts         local copy of the contract types the bundle uses
  src/manifest.ts      runtime manifest Core reads from dist/index.js
  src/service-client.ts  reads intera_service_url, calls the service
  src/items.datasource.ts  the "Hello .NET items" data source
  src/test-connection.ts   "Test connection" -> GET /health
  src/index.ts         CommonJS entry point
  test/bundle.test.mjs unit tests with a mocked fetch
service/
  Program.cs           .NET 11 minimal API: /health, /api/items
  Dockerfile           multi-stage (sdk -> aspnet), non-root, port 8080
```

## Service API

| Method and path | Answer |
| --- | --- |
| `GET /health` | `200 {"status":"ok"}` |
| `GET /api/items` | `200 [{"id","name","amount","updatedAt"}, ...]` |

## Build and test locally

From the repository root:

```bash
# Service image
docker build -t intera-hello-dotnet:1.0.0 integrations/intera/hello-dotnet/service
docker run --rm -d --name hello-dotnet -p 8080:8080 intera-hello-dotnet:1.0.0
curl -s localhost:8080/health
curl -s localhost:8080/api/items
docker rm -f hello-dotnet

# Bundle
cd integrations/intera/hello-dotnet/bundle
npm ci
npm run build
npm test
cd -

# Manifest pre-check and catalog artifact (out/intera-hello-dotnet-1.0.0.tar.gz)
npm ci
node scripts/validate-manifest.mjs integrations/intera/hello-dotnet
scripts/pack.sh integrations/intera/hello-dotnet
```

The packed manifest keeps the placeholder digest unless you pass a pushed
image: `scripts/pack.sh integrations/intera/hello-dotnet --image <registry>/hello-dotnet:1.0.0`
resolves its digest and writes it into the archive; add
`--stage-dir <dir>` to get the plugin folder for `intera-library publish`. See
[docs/testing-locally.md](../../../docs/testing-locally.md) and
[docs/publishing.md](../../../docs/publishing.md).

## Release

Tag `intera/hello-dotnet/v1.0.1`. The release workflow pushes
`ghcr.io/intera-ltd/intera/hello-dotnet:1.0.1` (linux/amd64 and linux/arm64), writes
its digest into the packed manifest and attaches
`intera-hello-dotnet-1.0.1.tar.gz` to the GitHub release.

## License

Apache-2.0, see [LICENSE](../../../LICENSE).
