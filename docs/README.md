# Documentation

| Document | What it covers |
| --- | --- |
| [Getting started](getting-started.md) | From an empty folder to a pull request |
| [Manifest reference](manifest-reference.md) | Every field of `manifest.yaml` |
| [Bundle contract](bundle-contract.md) | What Core expects from `dist/index.js` |
| [Docker services](docker-services.md) | Running your own service next to INTERA |
| [Testing locally](testing-locally.md) | Unit tests, the container, the artifact |
| [Publishing](publishing.md) | Releases, image digests, the catalog |
| [Review guidelines](review-guidelines.md) | What reviewers check in a pull request |
| [Security rules](security.md) | Images by digest, no secrets, safe bundles |

Words used throughout:

- **Core** — the INTERA application of one installation. It loads integration
  bundles and calls them.
- **Bundle** — the JavaScript entry point of an integration (`dist/index.js`),
  usually compiled from TypeScript.
- **Service** — an optional container with your own code, in any language.
  The bundle calls it over HTTP.
- **Integration Library** (catalog) — the global catalog of integrations at
  `https://library.intera-roles.com/api/v1`. Installations install from it in
  **Settings → Marketplace**.
- **Artifact** — what the catalog stores for a version: a `.tar.gz` with
  `manifest.yaml` and `dist/`.
- **Installation agent** — a small service of each installation (not an AI
  agent) that pulls and runs integration containers. Core itself has no
  access to Docker.
