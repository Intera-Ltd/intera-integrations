# Docker services

An integration can bring its own service: a container with code in any
language. The bundle stays thin and calls the service over HTTP.

## How an installation runs it

1. The administrator clicks **Install** in **Settings → Marketplace**.
2. Core downloads the artifact from the catalog and verifies the catalog
   signature, the digests and the archive contents before running anything.
3. Core asks the installation agent to deploy the container. The agent pulls
   `deployment.image` by digest (with the installation's registry login, if
   the operator configured one), starts it in the installation's internal
   network with `deployment.env` and the values of `deployment.settings`, and
   polls `deployment.health_path` until it answers 2xx.
4. The agent returns the container address. Core passes it to the bundle as
   `intera_service_url` and restarts to load the bundle.

On update the agent starts the new container next to the old one and switches
only when the new one is healthy; otherwise the old one keeps running. On
removal the container is stopped and deleted. When the administrator changes
the container settings, the container is redeployed the same way; Core is not
restarted.

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
| Public image preferred | Any installation can pull it; a private registry needs a login configured by each installation's operator (below) |

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
- `deployment.settings` is for values the administrator of the installation
  sets: an upstream API key, a region, a timeout. They are declared in the
  manifest ([reference](manifest-reference.md#deploymentsettings)) and reach
  the container as environment variables. Secrets (`kind: secret`) are stored
  encrypted by the installation and never shown by its API.
- Read settings at startup and fail with a clear message if a required one is
  missing. When the administrator changes a value, the agent starts a new
  container with it; the old one runs until the new one is healthy.
- A secret the **bundle** needs (not the service) stays a `secret` field of
  the connection (`configSchema`).

```yaml
deployment:
  type: docker
  image: ghcr.io/intera-ltd/billing@sha256:<64 hex>
  port: 8080
  health_path: /health
  env:
    ASPNETCORE_HTTP_PORTS: '8080'
  settings:
    - name: api_token
      label: API token
      kind: secret
      required: true
      env: BILLING__APITOKEN
    - { name: region, label: Region, kind: string, default: 'eu' }
```

### Reading settings in .NET

ASP.NET Core reads environment variables into configuration, and `__` in a
variable name is a section separator, so `BILLING__APITOKEN` is
`Billing:ApiToken` (keys are case-insensitive):

```csharp
var builder = WebApplication.CreateSlimBuilder(args);

// BILLING__APITOKEN -> Billing:ApiToken, REGION -> Region
var apiToken = builder.Configuration["Billing:ApiToken"];
if (string.IsNullOrEmpty(apiToken))
{
    Console.Error.WriteLine("Billing:ApiToken (BILLING__APITOKEN) is not set");
    return 1;
}
var region = builder.Configuration["Region"] ?? "eu";

var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/region", () => Results.Ok(new { region })); // never return the token
app.Run();
return 0;
```

A plain `Environment.GetEnvironmentVariable("REGION")` works too. In Node.js
it is `process.env.REGION`, in Python `os.environ["REGION"]`.

## Private registries

The image may be in a registry that needs credentials (ghcr.io with a token,
GitLab, Harbor). The credentials belong to the installation: its operator sets
them on the installation agent (`INTEGRATION_AGENT_REGISTRY_AUTH`). Never put
them in the manifest, the repository or the catalog. If the registry refuses
the pull, the installation or update stops with
`INTEGRATION_AGENT_IMAGE_PULL_DENIED`; on an update the previous version keeps
running.

For integrations in the public catalog a public image is simpler: every
installation can pull it without extra setup.

## Example

[`integrations/intera/hello-dotnet`](../integrations/intera/hello-dotnet/):
.NET 8 minimal API, multi-stage `Dockerfile` (`sdk` → `aspnet`), non-root
`app` user, port 8080, `GET /health`.
