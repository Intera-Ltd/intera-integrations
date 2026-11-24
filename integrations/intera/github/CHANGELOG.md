# Changelog

All notable changes to this integration are documented here. The format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the
version follows [Semantic Versioning](https://semver.org/).

## [3.2.1] - 2026-11-23

### Fixed

- Bounded the conditional ETag page cache with a 10-minute TTL, a 256-entry cap, and a 16 MiB byte budget with eager expiry sweeps and oldest-entry eviction.
- Bounded the in-process response cache to 1,000 entries and 16 MiB with eager expired sweeps and oldest-entry eviction.
- Required `https:` for `apiUrl` (allowing `http:` only on loopback `localhost`, `127.0.0.1`, `[::1]`) and rejected URL userinfo and fragments.

## [3.2.0] - 2026-11-21

### Added

- Integration Contract 2 catalog manifest (`manifest.yaml`) and packaging for `intera-integrations`.
- Query Contract 1.5.0 support: `snapEdge` + `offset` relative dates, field-to-field comparisons (`FieldRefValue`), `dateDiff` / `now` row formulas, and expression filter conditions.
