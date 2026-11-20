# INTERA Integrations

Integrations for INTERA built by
INTERA and by third-party developers, in one repository. Each integration here
is released as an artifact of the INTERA **Integration Library**, the catalog
that installations browse and install from in **Settings → Marketplace**
(public API: `https://library.intera-roles.com/api/v1`).

## What an integration is

- A **bundle**: a small JavaScript module (`dist/index.js`, usually written in
  TypeScript) that INTERA Core loads. It declares data sources and reads data
  for metrics, assets and dashboards.
- Optionally, a **service**: your own code in a Docker container, in any
  language. When an administrator installs the integration, the installation
  agent pulls the image by digest and starts it; Core passes its address to
  the bundle as `intera_service_url`.
- A **`manifest.yaml`** that describes the version for the catalog: id,
  compatibility, categories, and how to deploy the service.

## Repository layout

```
integrations/<developer>/<integration>/
  manifest.yaml        catalog manifest
  bundle/              TypeScript bundle -> dist/
  service/             optional: Dockerfile and service code
  README.md
  CHANGELOG.md
templates/
  bundle-only/         bundle that calls an external API
  bundle-with-service/ bundle + container service
docs/                  guides and references
scripts/               manifest checks, packing, image digest, local runs
.github/               CI, issue and pull request templates
```

## Integrations

| Integration | Developer | Shape | Description |
| --- | --- | --- | --- |
| [Hello .NET](integrations/intera/hello-dotnet/) | `intera` | bundle + .NET 8 service | Sample data source served by a .NET container |

## Quick start

```bash
npm ci                                               # repository tooling
cd integrations/intera/hello-dotnet/bundle && npm ci && npm test && cd -
docker build -t intera-hello-dotnet:1.0.0 integrations/intera/hello-dotnet/service
node scripts/validate-manifest.mjs integrations/intera/hello-dotnet
scripts/pack.sh integrations/intera/hello-dotnet     # -> out/intera-hello-dotnet-1.0.0.tar.gz
```

Then read [Getting started](docs/getting-started.md) to build your own.

## Documentation

- [Getting started](docs/getting-started.md)
- [Manifest reference](docs/manifest-reference.md)
- [Bundle contract](docs/bundle-contract.md)
- [Docker services](docs/docker-services.md)
- [Testing locally](docs/testing-locally.md)
- [Publishing](docs/publishing.md)
- [Review guidelines](docs/review-guidelines.md)
- [Security rules](docs/security.md)

## Contributing

Contributions are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) and the
[Code of Conduct](CODE_OF_CONDUCT.md). Report vulnerabilities privately as
described in [SECURITY.md](SECURITY.md).

## License

The license of this repository has not been chosen yet. Until a `LICENSE`
file is added, no license is granted beyond what GitHub's terms allow.
