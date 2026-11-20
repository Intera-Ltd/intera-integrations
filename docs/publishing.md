# Publishing

A version travels from this repository to installations in four steps:
**pull request → release → catalog review → Marketplace**.

## 1. Pull request

Bump `version` in `manifest.yaml` and in `bundle/src/constants.ts` (and
`bundle/package.json`), add a `CHANGELOG.md` entry, open a pull request. CI
must be green and a maintainer must approve it.

Versions follow SemVer and are **immutable** in the catalog: a published
version can never be replaced, only deprecated. Any change, even a rebuild,
is a new version. Declare breaking changes in the changelog and in the pull
request; the catalog warns administrators before such an update.

## 2. Release

After the merge a maintainer pushes a tag:

```bash
git tag acme/billing/v1.2.0
git push origin acme/billing/v1.2.0
```

The tag format is `<developer>/<integration>/v<version>`, and the version
must equal `manifest.yaml`. The release workflow
(`.github/workflows/release.yml`), which can also be started by hand from the
Actions tab:

1. checks the tag against the folder and the manifest;
2. builds and tests the bundle;
3. builds the service image for `linux/amd64` and `linux/arm64` and pushes
   `ghcr.io/intera-ltd/<integration>:<version>`;
4. writes the pushed digest into the packed manifest
   (`deployment.image: ghcr.io/intera-ltd/<integration>@sha256:...`);
5. packs `<developer>-<integration>-<version>.tar.gz` (`manifest.yaml` +
   `dist/`) and attaches it to a GitHub release, with the digest in the
   release notes and the job summary. The archive is the plugin folder that
   `intera-library publish` takes (step 3).

It uses only the built-in `GITHUB_TOKEN`. The manifest in the repository
keeps the placeholder digest; the release asset is the source of truth.

## 3. Catalog

The version is uploaded to the Integration Library with the catalog CLI
`intera-library`, as a draft, and submitted for review. Uploading needs a
developer account and token in the catalog; INTERA issues them. Ask in an
issue if you want to publish under your own developer slug.

The CLI works on a **plugin folder**: `manifest.yaml` at the root and the
built bundle in `dist/`, nothing else. The `deployment.image` in that
manifest must already hold the real digest of the pushed image. `id` and
`version` of the upload are taken from `manifest.yaml`.

Get the plugin folder in one of two ways:

```bash
# a) from the release asset: the archive is exactly manifest.yaml + dist/
mkdir -p out/acme-billing-1.2.0
tar -xzf acme-billing-1.2.0.tar.gz -C out/acme-billing-1.2.0

# b) from this repository, with the digest of an image you pushed
scripts/pack.sh integrations/acme/billing \
  --image ghcr.io/intera-ltd/billing@sha256:<64 hex> --release \
  --stage-dir out/acme-billing-1.2.0
```

Then check it offline and publish it:

```bash
intera-library pack out/acme-billing-1.2.0     # no network, no token: builds the
                                               # archive, prints its SHA-256
export INTERA_LIBRARY_TOKEN=...                # your developer token, never commit it
intera-library publish out/acme-billing-1.2.0 \
  --release-notes-file integrations/acme/billing/CHANGELOG.md --submit
```

| Command | What it does |
| --- | --- |
| `intera-library pack <plugin-dir> [--out file.tar.gz]` | Checks `manifest.yaml` with the catalog's own rules and packs a deterministic archive. No network. |
| `intera-library publish <plugin-dir> [--channel <key>] [--release-notes <text> \| --release-notes-file <file>] [--breaking-changes] [--submit]` | Packs, uploads the version as a draft and, with `--submit`, sends it for review. Without `--submit` the draft stays; submit it later with `intera-library version submit <id> <version>`. |

`publish` is safe to repeat: the same version with the same archive changes
nothing, so a retry after a network error is fine. The same version with a
different archive is refused (409): a new build needs a new version.
`--breaking-changes` marks the version so that installations warn the
administrator before the update.

On upload the catalog validates `manifest.yaml` and the archive in full and
rejects unknown categories, a version that does not match and an already
used version. INTERA reviews the version and publishes it; publishing signs
the artifact with the INTERA key. Installations verify that signature before
they install anything.

## 4. Marketplace

Once published, the version appears in **Settings → Marketplace** of every
installation whose INTERA version and Integration Contract match
`compatibility`. Installed copies see it as an update. If the version
declares `deployment.settings`, the administrator fills them in the install
or update dialog; a new required setting has to be filled before the update
runs.

The public catalog is readable without a token:

```bash
curl -s https://library.intera-roles.com/api/v1/integrations/acme.billing
curl -s https://library.intera-roles.com/api/v1/integrations/acme.billing/versions
```
