# Changelog

All notable changes to this integration are documented here. The format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the
version follows [Semantic Versioning](https://semver.org/).

## [3.3.0] - 2026-11-24

### Changed

- Adopted platform-managed `INTERA_SERVICE_TOKEN` (`intera_service_token`) authentication on every service endpoint except `GET /health`, and removed manual `api_access_token` / `apiBaseUrl` / `accessToken` settings (requires Core `>=0.5.94`).

## [3.2.1] - 2026-11-23

### Changed

- Require inbound access token authentication on `POST /oauth/refresh`, enforce startup configuration and HTTPS validation (`ValidateOnStart`), quiet `HttpClient` info logging, and pin `.NET 10` container base image digests.

## [3.2.0] - 2026-11-21

### Added

- Integration Contract 2 catalog manifest (`manifest.yaml`), containerized .NET 10 service (`service/Dockerfile`), and `intera_service_url` support.
- `testConnection` probe and `context.signal` cancellation forwarding.
- Query Contract 1.5.0 support (`snapEdge` + `offset` relative dates, `FieldRefValue`, `dateDiff` / `now` row formulas, expression filters).
