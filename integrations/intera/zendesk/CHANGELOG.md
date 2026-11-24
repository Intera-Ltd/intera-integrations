# Changelog

All notable changes to this integration are documented here. The format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the
version follows [Semantic Versioning](https://semver.org/).

## [4.2.0] - 2026-11-24

### Changed

- Adopted platform-managed `INTERA_SERVICE_TOKEN` (`intera_service_token`) authentication on every service endpoint except `GET /health`, and removed manual `api_access_token` / `apiBaseUrl` / `accessToken` settings (requires Core `>=0.5.94`).
- Enabled persistent volume storage (`deployment.data_path: /app/App_Data`) with AES-256-GCM encrypted OAuth token persistence via `INTERA_SERVICE_DATA_KEY` and startup path-traversal validation.

## [4.1.1] - 2026-11-23

### Changed

- Return `{ status: 'needs_authorization', authorizeUrl }` from `testConnection` instead of throwing, auto-registering `oauth-callback` from `context.webhookUrls` when needed (requires Core `>=0.5.25`).
- Report Marketplace container environment variable names (`ZENDESK__CLIENTID`, `ZENDESK__CLIENTSECRET`, `ZENDESK__REDIRECTURI`) in diagnostics.
- Add startup configuration and HTTPS validation (`ValidateOnStart`) and pin `.NET 10` container base image digests.

## [4.1.0] - 2026-11-21

### Added

- Integration Contract 2 catalog manifest (`manifest.yaml`), containerized .NET 10 service (`service/Dockerfile`), and `intera_service_url` support.
- `context.signal` cancellation forwarding and Query Contract 1.5.0 support (`snapEdge` + `offset`, `FieldRefValue`, `dateDiff` / `now` row formulas, expression filters).
