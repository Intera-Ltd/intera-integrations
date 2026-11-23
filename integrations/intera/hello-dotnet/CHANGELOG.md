# Changelog

All notable changes to this integration are documented here. The format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the
version follows [Semantic Versioning](https://semver.org/).

## [1.0.1] - 2026-11-24

### Changed

- Namespace service image as `ghcr.io/intera-ltd/intera/hello-dotnet` and pin `.NET 8` base images by digest.

## [1.0.0] - 2026-11-20

### Added

- .NET 8 service with `GET /health` and `GET /api/items`.
- Bundle with the "Hello .NET items" data source and a connection test.
