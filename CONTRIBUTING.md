# Contributing

Thank you for building an integration for INTERA. This repository holds
integrations by INTERA and by third-party developers; everything goes through
pull requests and review.

## Before you start

- Read [Getting started](docs/getting-started.md) and the
  [security rules](docs/security.md).
- For a new integration, open a **New integration** issue first. It is the
  place to agree on the slug, categories and whether you need a service.
- For a bug in an existing integration, open a **Bug report** issue.
- Do not report vulnerabilities in public issues; see [SECURITY.md](SECURITY.md).

## Workflow

1. Fork the repository and create a branch.
2. Add or change one integration per pull request:
   `integrations/<developer>/<integration>/`. Changes to the repository
   itself (docs, templates, scripts, CI) go in separate pull requests.
3. Build and test locally ([Testing locally](docs/testing-locally.md)):

   ```bash
   npm ci
   (cd integrations/<developer>/<integration>/bundle && npm ci && npm test)
   docker build integrations/<developer>/<integration>/service   # if there is one
   node scripts/validate-manifest.mjs integrations/<developer>/<integration>
   ```

4. Update `README.md` and `CHANGELOG.md` of the integration and bump the
   version where the change needs a release.
5. Open the pull request and fill in the template. CI must pass.
6. A maintainer reviews it against the
   [review guidelines](docs/review-guidelines.md). Releases are tagged by
   maintainers after the merge ([Publishing](docs/publishing.md)).

## Conventions

- Text in the repository is in English.
- TypeScript in `strict` mode, no runtime dependencies in bundles, a
  committed `package-lock.json`.
- Commit messages in the imperative mood: "Add billing data source".
- Keep changes focused; do not reformat unrelated files.

## Developer slugs

The developer slug must match your developer account in the Integration
Library. INTERA issues developer accounts; ask in your **New integration**
issue. Until you have one, you can still contribute under review.

## Code of Conduct

By taking part you agree to the [Code of Conduct](CODE_OF_CONDUCT.md).
