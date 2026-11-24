# Changelog

All notable changes to this integration are documented here. The format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the
version follows [Semantic Versioning](https://semver.org/).

## [3.4.0] - 2026-11-24

### Changed

- Adopted platform-managed `INTERA_SERVICE_TOKEN` (`intera_service_token`) authentication on every service endpoint except `GET /health`, and removed manual `api_access_token` / `apiBaseUrl` / `accessToken` settings (requires Core `>=0.5.94`).
- Enabled persistent volume storage (`deployment.data_path: /app/App_Data`) with AES-256-GCM encrypted OAuth token persistence via `INTERA_SERVICE_DATA_KEY` and startup path-traversal validation.

## [3.3.0] - 2026-11-23

### Added

- Automatic OAuth 2.0 callback relay through INTERA Core's `oauth-callback` webhook handler (`PUT /oauth/redirect-uri`, `registerWebhook`, `onWebhook`), making `redirect_uri` optional.
- `needs_authorization` + `authorizeUrl` handshake in `testConnection` (requires Core `>=0.5.25`).
- Startup configuration and HTTPS validation (`ValidateOnStart`) and pinned `.NET 10` container base image digests.

## [3.2.0] - 2026-11-21

### Added

- Integration Contract 2 catalog manifest (`manifest.yaml`), containerized .NET 10 service (`service/Dockerfile`), and `intera_service_url` support.
- `context.signal` cancellation forwarding and Query Contract 1.5.0 support (`snapEdge` + `offset`, `FieldRefValue`, `dateDiff` / `now` row formulas, expression filters).
