# Review guidelines

Maintainers check every pull request against this list. Authors can use it
as a self-check before asking for review.

## Layout and metadata

- [ ] Folder is `integrations/<developer>/<integration>/`; `id` is
      `<developer>.<integration>`; the integration slug is unique in the
      repository.
- [ ] `manifest.yaml` passes `scripts/validate-manifest.mjs`; versions are
      quoted strings.
- [ ] `version` is the same in `manifest.yaml`, the bundle manifest and
      `bundle/package.json`; `runtime_id` equals the bundle `id` and did not
      change from the previous version.
- [ ] `compatibility.intera_min` is the lowest INTERA version the
      integration was tested with; `integration_contract` is current.
- [ ] Categories and roles are catalog keys and describe the integration
      honestly.
- [ ] `README.md` explains what the integration does, its configuration,
      data sources and fields; `CHANGELOG.md` has an entry for the version.
- [ ] Breaking changes (removed fields, renamed data sources, changed
      identity) are called out.

## Bundle

- [ ] CommonJS `dist/index.js`, no runtime dependencies (or bundled into
      `dist/` with a good reason), `package-lock.json` committed.
- [ ] Every outbound call has a timeout and forwards `context.signal`.
- [ ] Configuration is validated before any request; no hardcoded hosts,
      ports or credentials; the service address comes from
      `intera_service_url`.
- [ ] Errors are clear and contain no secrets or raw upstream bodies.
- [ ] No `eval`, `new Function`, child processes, file writes, or network
      calls other than the declared upstream API or service.
- [ ] Unit tests cover the cases in [Testing locally](testing-locally.md).

## Service (Docker)

- [ ] Multi-stage build, official base images, runs as non-root.
- [ ] Listens on `deployment.port`; `deployment.health_path` answers 2xx only
      when ready.
- [ ] `deployment.env` holds public values only.
- [ ] No secrets, test credentials or private data baked into the image.
- [ ] Logs go to stdout/stderr and never print secrets.

## Repository hygiene

- [ ] No secrets, tokens, internal hostnames, IP addresses or customer names
      anywhere, including tests, fixtures and commit messages.
- [ ] Files outside the integration folder changed only when the change is
      about the repository itself.

See also the [security rules](security.md).
