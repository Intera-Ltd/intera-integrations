# Changelog

All notable changes to this integration are documented here. The format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the
version follows [Semantic Versioning](https://semver.org/).

## [3.2.0] - 2026-11-24

### Added

- Integration Contract 2 catalog manifest (`manifest.yaml`), containerized stateless .NET 10 service (`service/Dockerfile`), and platform-managed `intera_service_url` + `intera_service_token` (`INTERA_SERVICE_TOKEN`) authentication.
- Startup configuration and HTTPS validation (`TeamCityOptionsValidator` with `ValidateOnStart`), requiring an authenticated TeamCity bearer token and disabling unauthenticated guest access.
- `context.signal` cancellation forwarding, bounded response reading, secret redaction, and Query Contract 1.5.0 support.
