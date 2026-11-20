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
   release notes and the job summary.

It uses only the built-in `GITHUB_TOKEN`. The manifest in the repository
keeps the placeholder digest; the release asset is the source of truth.

## 3. Catalog

The artifact is uploaded to the Integration Library as a draft version and
submitted for review. Uploading needs a developer account and token in the
catalog; INTERA issues them. Ask in an issue if you want to publish under
your own developer slug.

```bash
export INTERA_LIBRARY_TOKEN=...        # your developer token, never commit it
intera-library version upload acme.billing acme-billing-1.2.0.tar.gz --version 1.2.0 \
  --release-notes-file CHANGELOG.md
intera-library version submit acme.billing 1.2.0
```

On upload the catalog validates `manifest.yaml` and the archive in full and
rejects unknown categories, a version that does not match and an already
used version. INTERA reviews the version and publishes it; publishing signs
the artifact with the INTERA key. Installations verify that signature before
they install anything.

**Coming in the next platform release:** `intera-library pack <dir>` (build
the artifact from an integration folder) and `intera-library publish <dir>`
(pack, upload and submit in one step).

## 4. Marketplace

Once published, the version appears in **Settings → Marketplace** of every
installation whose INTERA version and Integration Contract match
`compatibility`. Installed copies see it as an update.

The public catalog is readable without a token:

```bash
curl -s https://library.intera-roles.com/api/v1/integrations/acme.billing
curl -s https://library.intera-roles.com/api/v1/integrations/acme.billing/versions
```
