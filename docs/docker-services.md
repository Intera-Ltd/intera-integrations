# Docker services

An integration can bring its own service: a container with code in any
language. The bundle stays thin and calls the service over HTTP.

## How an installation runs it

1. The administrator clicks **Install** in **Settings → Marketplace**.
2. Core downloads the artifact from the catalog and verifies the catalog
   signature, the digests and the archive contents before running anything.
3. Core asks the installation agent to deploy the container. The agent pulls
   `deployment.image` by digest, starts it in the installation's internal
   network with `deployment.env`, and polls `deployment.health_path` until it
   answers 2xx.
4. The agent returns the container address. Core passes it to the bundle as
   `intera_service_url` and restarts to load the bundle.

On update the agent starts the new container next to the old one and switches
only when the new one is healthy; otherwise the old one keeps running. On
removal the container is stopped and deleted.

## Requirements for the image

| Rule | Why |
| --- | --- |
| Listen on `deployment.port` on all interfaces (`0.0.0.0`) | The bundle calls `http://<container>:<port>` inside the network |
| Answer `GET <health_path>` with 2xx only when ready | The agent waits for it before installing the bundle |
| Run as a **non-root** user | Least privilege; reviewers reject root images |
| Keep no state you cannot lose, or document it | Updates replace the container |
| Log to stdout/stderr, never log secrets | Logs are how failures are diagnosed |
| Multi-stage build, small runtime base image | Smaller attack surface and faster pulls |
| Build for `linux/amd64` and `linux/arm64` | Installations run on both; the release workflow does this |
| Pullable without credentials | Private registries are coming in the next platform release |

The port is not published outside the installation network and the container
is started with resource limits and `no-new-privileges`. It can reach the
internet if your service needs to call an upstream API.

## Talking to the service

- Plain HTTP with JSON is the simplest contract. Version your paths
  (`/api/v1/...`) if the bundle and the service may drift.
- The bundle must handle the service being unavailable: return a clear error,
  do not hang. Use timeouts and forward `context.signal`.
- The service is reachable only from inside the installation network, but do
  not treat that as authentication for anything sensitive.

## Configuration and secrets

- `deployment.env` is for **public, fixed** values (log level, port, feature
  switches). The manifest is public and stored in the catalog.
- Values and secrets set by the administrator (`deployment.settings`) are
  **coming in the next platform release**. Until then, an integration that
  needs a secret takes it as a `secret` field of the connection
  (`configSchema`) and passes it from the bundle to the service per request,
  for example in a header.

## Example

[`integrations/intera/hello-dotnet`](../integrations/intera/hello-dotnet/):
.NET 8 minimal API, multi-stage `Dockerfile` (`sdk` → `aspnet`), non-root
`app` user, port 8080, `GET /health`.
