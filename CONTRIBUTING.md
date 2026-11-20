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
   version where the change needs a release. Check its `license`
   ([Licensing](#licensing)).
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

## Licensing

This repository is licensed under the [Apache License 2.0](LICENSE).

**Contributions are inbound = outbound.** As section 5 of the license says,
anything you intentionally submit for inclusion (a pull request, a patch, a
suggested change) is licensed under Apache-2.0, without additional terms or
conditions, unless you explicitly state otherwise. There is no separate
contributor agreement. Submit only work you have the right to license this
way, and keep the copyright and license notices of any code you bring in.

**Integrations by third-party developers** follow the same default:

- With no other declaration, an integration folder is Apache-2.0. Set
  `license: Apache-2.0` in `manifest.yaml` so the catalog shows it (the
  templates do).
- To use your own license for your integration, put its full text in
  `integrations/<developer>/<integration>/LICENSE` and set `license` in
  `manifest.yaml` to its [SPDX identifier](https://spdx.org/licenses/) (for
  example `MIT`, `BSD-3-Clause`) or a short name of up to 128 characters.
  That license covers that folder only; everything else stays Apache-2.0.
- The license must allow the maintainers to build, release and publish the
  bundle and the service image, and installations to run them. Reviewers
  check it.
- The templates are Apache-2.0. If your folder starts from a template and
  uses another license, the copied code keeps the conditions of section 4 of
  Apache-2.0 (a copy of the license, retained notices).
- `scripts/validate-manifest.mjs` fails when `license` names another license
  but the folder has no `LICENSE` file, or when the folder has a `LICENSE`
  file but `manifest.yaml` has no `license`. Only `manifest.yaml` and `dist/`
  go into the catalog artifact, so the `license` field is what installations
  see.

## Code of Conduct

By taking part you agree to the [Code of Conduct](CODE_OF_CONDUCT.md).
