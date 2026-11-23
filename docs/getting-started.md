# Getting started

This guide takes you from nothing to a pull request with a working
integration. It assumes Node.js 20 or newer, npm and, for integrations with a
service, Docker.

## 1. Pick a shape

An INTERA integration is always a **bundle**: a CommonJS JavaScript module
(`dist/index.js`) that Core loads. It can optionally come with a **service**:
a container with your own code.

| You need | Use |
| --- | --- |
| Read an HTTP API with a key the administrator enters | [`templates/bundle-only`](../templates/bundle-only/) |
| Heavier logic, another language, a long-lived process, local state | [`templates/bundle-with-service`](../templates/bundle-with-service/) |

[`integrations/intera/hello-dotnet`](../integrations/intera/hello-dotnet/) is a
complete example of the second shape with a .NET 8 service.

## 2. Choose your ids

- **Developer slug** — your organization in the catalog, for example `acme`.
  Lowercase letters, digits and single hyphens.
- **Integration slug** — for example `billing`. Same rules. Unique within
  `<developer>`; the image is published as
  `ghcr.io/intera-ltd/<developer>/<integration>`.
- **Catalog id** — `<developer>.<integration>`: `acme.billing`.
- **Runtime id** — the id of the integration inside the bundle, for example
  `acme_billing`. Lowercase letters and digits, words joined by `-` or `_`.
  Connections in an installation belong to it, so never change it after the
  first release.

## 3. Copy a template

```bash
mkdir -p integrations/acme
cp -R templates/bundle-with-service integrations/acme/billing
cd integrations/acme/billing
grep -rl 'your-org\|your-integration\|your_integration' . | xargs sed -i.bak \
  -e 's/your-org/acme/g' -e 's/your-integration/billing/g' -e 's/your_integration/acme_billing/g'
find . -name '*.bak' -delete
```

Then edit:

- `manifest.yaml` — name, version, compatibility, categories, roles, license
  ([reference](manifest-reference.md));
- `bundle/src/constants.ts` — the same runtime id and version;
- `bundle/src/*.datasource.ts` — your data sources ([contract](bundle-contract.md));
- `service/` — your service ([Docker services](docker-services.md));
- `README.md` and `CHANGELOG.md`.

## 4. Build and test

```bash
cd bundle
npm install          # creates package-lock.json, commit it
npm test             # builds dist/ and runs the unit tests
cd ..
docker build -t billing:dev service
cd ../../..
npm ci && node scripts/validate-manifest.mjs integrations/acme/billing
```

More in [Testing locally](testing-locally.md).

## 5. Open a pull request

Fill the pull request template. CI builds and tests the integrations you
changed, builds the image and checks `manifest.yaml`. A maintainer reviews
it against the [review guidelines](review-guidelines.md).

After the merge, a maintainer tags the release and the version goes to the
catalog for review; see [Publishing](publishing.md).
