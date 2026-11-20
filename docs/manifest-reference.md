# manifest.yaml reference

`manifest.yaml` describes one version of an integration for the Integration
Library. It sits at the root of the integration folder and at the root of the
artifact. The catalog validates it when a version is uploaded and rejects the
whole upload on any problem, listing all of them at once.
`scripts/validate-manifest.mjs` runs a quicker subset of the same checks in CI.

## Example

```yaml
id: acme.billing
name: Acme Billing
version: '1.2.0'
runtime_id: acme_billing
developer:
  id: acme
compatibility:
  intera_min: '0.5.0'
  integration_contract: 2
categories: [billing]
roles: [financial]
capabilities:
  datasources: true
  metrics: true
license: Apache-2.0
provides: [finance.accounting@1.0.0]
deployment:
  type: docker
  image: ghcr.io/intera-ltd/billing@sha256:<64 hex>
  port: 8080
  health_path: /health
  env:
    LOG_LEVEL: info
```

## Fields

| Field | Required | Rules |
| --- | --- | --- |
| `id` | yes | `<developer>.<integration>`, lowercase `a-z 0-9 -` segments joined by dots, up to 128 characters. Starts with `developer.id` and a dot. |
| `name` | yes | Display name, up to 128 characters. |
| `version` | yes | SemVer `X.Y.Z`, **quoted** (`'1.0.0'`). Must equal the version of the upload and the `version` in the bundle. A version is immutable: a new build is a new version. |
| `runtime_id` | for Marketplace | Id of the integration inside the bundle (`manifest.id` in `dist/index.js`): `^[a-z0-9]+(?:[-_][a-z0-9]+)*$`, up to 64 characters. Required to install from Marketplace. Must not change between versions: connections belong to it. |
| `developer.id` | yes | Developer slug in the catalog: lowercase words joined by single hyphens, up to 64 characters. |
| `compatibility` | yes | See below. |
| `categories` | no | Catalog category keys. Unknown keys are rejected. |
| `roles` | no | Catalog Role Package keys. Unknown keys are rejected. |
| `capabilities` | no | Map of booleans: `datasources`, `metrics`, `external_assets`, `widgets`, `writeback`. An absent one means `false`. Other keys are rejected. |
| `license` | no | SPDX identifier or a short license name, up to 128 characters. |
| `provides` | no | Data standards the integration implements: `contractKey@X.Y.Z`, for example `finance.accounting@1.0.0`. |
| `deployment` | no | How the installation runs the integration besides its bundle. See below. |

Unknown fields, duplicate keys, YAML tags and files larger than 64 KiB are
rejected. Lists hold at most 50 items.

### `compatibility`

```yaml
compatibility:
  intera_min: '0.5.0'      # lowest INTERA version, inclusive
  intera_max: '1.x'        # optional: X.Y.Z, X.Y.x or X.x, inclusive
  integration_contract: 2  # Integration Contract the bundle is written against
```

The nested form `intera: { min, max }` is accepted too; use one form, not both.
The catalog offers an installation only versions whose range contains its
INTERA version and whose contract it supports. The current Integration
Contract is **2**.

### Categories and Role Packages

Read the current keys from the public catalog:

```bash
curl -s https://library.intera-roles.com/api/v1/categories
curl -s https://library.intera-roles.com/api/v1/role-packages
```

Starting categories: `accounting`, `erp`, `crm`, `sales-support`,
`customer-support`, `banking`, `network-support`, `billing`, `monitoring`,
`navigation`, `logistics`, `manufacturing`, `shipmanagement`, `weather`,
`external-data`, `document-management`, `payment`.

Starting Role Packages: `telecom`, `shipmanagement`, `logistics`,
`manufacturing`, `financial`, `marketplaces`, `farming`.

Need a new key? Ask in the pull request; the catalog team adds keys.

### `deployment`

Absent or `type: none`: Core loads the bundle only.

`type: docker`: the installation also runs your container.

| Field | Required | Rules |
| --- | --- | --- |
| `type` | yes | `none` or `docker`. |
| `image` | yes | Image **pinned by digest**: `<registry>/<repository>@sha256:<64 hex>`, lowercase, up to 512 characters. Tags are not accepted. |
| `port` | yes | Container port the bundle calls, 1–65535. |
| `health_path` | no | Absolute path the agent polls until it answers 2xx, for example `/health`. Strongly recommended. |
| `env` | no | Fixed environment variables: up to 50, names `^[A-Z_][A-Z0-9_]*$`, values quoted strings up to 4096 characters. **Public values only** — never secrets. |

In this repository, `deployment.image` holds a placeholder digest
(`@sha256:000…000`). The release workflow builds the image, pushes it and
writes the real digest into the packed manifest. See
[Publishing](publishing.md).

### Coming in the next platform release

These are being added to the platform and are **not accepted yet** by the
catalog. Do not use them until this document says they are available.

- **`deployment.settings`** — values and secrets of the container that the
  administrator sets at installation time (for example an upstream API key),
  instead of fixed `env` values.
- **Private registries** — images from registries that need credentials.
  Until then, images must be pullable anonymously.

## Two manifests

An integration has two manifests that must agree:

| | `manifest.yaml` | `manifest` export of `dist/index.js` |
| --- | --- | --- |
| Read by | the catalog and Marketplace | Core at startup |
| Id | `id` = `acme.billing` | `id` = `runtime_id` = `acme_billing` |
| Version | `version` | `version` — **must be equal** |
| Contract | `compatibility.integration_contract` | `compatibility.integration_contract` |

After an install or update, Core checks that the loaded bundle reports the
version from `manifest.yaml`; otherwise the update is rolled back.
`scripts/validate-manifest.mjs` checks both pairs when `bundle/dist` is built.
