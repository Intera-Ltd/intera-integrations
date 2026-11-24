# Changelog

All notable changes to this integration are documented here. The format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the
version follows [Semantic Versioning](https://semver.org/).

## [3.2.0] - 2026-11-24

### Added

- Integration Contract 2 catalog manifest (`manifest.yaml`), containerized .NET 10 service (`service/Dockerfile`), authored bank SVG icon, and encrypted persistent OAuth volume (`data_path: /app/App_Data`).
- Automatic OAuth 2.0 callback relay through INTERA Core's `oauth-callback` webhook handler (`PUT /oauth/redirect-uri`, `GET /oauth/authorize-url` with single-use `state`, `GET /oauth/callback`, `registerWebhook`, `onWebhook`).
- `needs_authorization` + `authorizeUrl` handshake in `testConnection` and mandatory `intera_service_token` bearer authentication across all service endpoints (requires Core `>=0.5.94`).
- In-memory mTLS client certificate loading from PEM certificate + private key (`EUROBANK__CLIENTCERTIFICATEPEM` + `EUROBANK__CLIENTPRIVATEKEYPEM`) or Base64 PKCS#12 (`EUROBANK__CLIENTCERTIFICATEPFXBASE64`), plus startup configuration validation (`ValidateOnStart`).
