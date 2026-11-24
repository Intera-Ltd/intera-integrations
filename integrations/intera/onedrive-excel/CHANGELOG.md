# Changelog

All notable changes to this integration are documented here. The format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the
version follows [Semantic Versioning](https://semver.org/).

## [3.3.0] - 2026-11-24

### Added

- Integration Contract 2 catalog manifest (`manifest.yaml`), spreadsheet SVG icon (`assets/icon.svg`), documentation, and `node:test` bundle verification suite for `intera-integrations`.
- Query Contract 1.5.0 support (`types.ts`, `relative-date.ts`, `query-evaluator.ts`, `query-shape.ts`): `relativeDate` with `snapEdge` and post-snap `offset`, field-to-field comparisons (`FieldRefValue`), `dateDiff` and `now` row expressions, and expression filter normalization.

### Changed

- Propagated `context.signal` (`AbortSignal`) combined with per-request `AbortSignal.timeout` across Entra ID token acquisition, Microsoft Graph metadata calls, and workbook downloads.
- Enforced streaming byte caps on workbook downloads (`64 MiB`) and Graph/OAuth JSON responses (`32 MiB`), scoped in-memory workbook and token LRU caches by SHA-256 credential fingerprint, validated download/redirect URLs, and scrubbed secrets and raw non-JSON upstream bodies from error messages.
