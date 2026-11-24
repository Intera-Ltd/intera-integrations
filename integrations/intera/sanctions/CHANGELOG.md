# Changelog

All notable changes to this integration are documented here. The format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the
version follows [Semantic Versioning](https://semver.org/).

## [1.2.1] - 2026-11-23

### Fixed

- Enforced `LIST_MAX_STALE_MS` during `retryAfter` backoff and avoided repeated failed downloads during backoff by caching the last error reason.
- Guarded XML numeric character entity decoding against out-of-range code points and UTF-16 surrogates, replacing invalid references with `U+FFFD`.
- Added a 64 MiB streaming download cap, detached extracted record strings from the source XML buffer, and released intermediate XML nodes immediately after parsing.
- Required `https:` for `eu_list_url` (allowing `http:` only on loopback `localhost`, `127.0.0.1`, `[::1]`) and rejected URL userinfo and fragments.

## [1.2.0] - 2026-11-21

### Added

- Integration Contract 2 catalog manifest (`manifest.yaml`) and packaging for `intera-integrations`.
- Query Contract 1.5.0 support: `snapEdge` + `offset` relative dates, field-to-field comparisons (`FieldRefValue`), `dateDiff` / `now` row formulas, expression filter conditions, and `context.signal` cancellation.
