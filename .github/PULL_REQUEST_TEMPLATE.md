## What

<!-- What does this pull request add or change, and why? -->

## Integration

- Folder: `integrations/<developer>/<integration>/`
- Version: <!-- e.g. 1.2.0 -->
- Shape: <!-- bundle only / bundle + service -->
- Breaking changes: <!-- none, or what breaks for existing connections -->

## How it was tested

<!-- Commands you ran and what you checked; see docs/testing-locally.md -->

- [ ] `npm test` in `bundle/`
- [ ] `docker build` of `service/` (if any) and a manual call of the health path
- [ ] `node scripts/validate-manifest.mjs <folder>`
- [ ] `scripts/pack.sh <folder>` produced an archive with only `manifest.yaml` and `dist/`

## Checklist

- [ ] One integration per pull request (or a repository-only change)
- [ ] Version bumped in `manifest.yaml`, the bundle and `package.json`; `CHANGELOG.md` updated
- [ ] `README.md` describes configuration and data sources
- [ ] No secrets, internal hostnames, IP addresses or customer names anywhere
- [ ] `deployment.image` keeps the placeholder digest (CI writes the real one)
- [ ] I followed the [review guidelines](../docs/review-guidelines.md) and [security rules](../docs/security.md)
