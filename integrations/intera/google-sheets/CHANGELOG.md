# Changelog

All notable changes to this integration are documented here. The format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the
version follows [Semantic Versioning](https://semver.org/).

## [1.3.1] - 2026-11-24

### Added

- Integration Contract 2 catalog manifest (`manifest.yaml`), documentation, and `node:test` bundle verification suite for `intera-integrations`.
- Query shape helper (`query-shape.ts`) alongside Query Contract 1.5.0 evaluation (`relativeDate` with `snapEdge` and `offset`, `FieldRefValue`, `dateDiff`, `now`, and expression filters).

### Changed

- Propagated `context.signal` (`AbortSignal`) combined with per-request `AbortSignal.timeout` across OAuth token exchange and Google Sheets API requests.
- Enforced streaming byte caps on Google OAuth and Sheets API responses (`64 MiB`), bounded the in-memory OAuth token cache with LRU eviction keyed by SHA-256 credential fingerprint, validated `token_uri` HTTPS/loopback rules, and scrubbed raw non-JSON upstream bodies and credentials from error messages.

## [1.3.0] - 2026-11-20

### Added

- Query Contract 1.5.0 support in `query-evaluator.ts` and `relative-date.ts`.
- Multi-tab dynamic provisioning (`gsheet-rows` primary tab plus `gsheet-tab-<gid>` per additional grid tab) and `testConnection` probe.
