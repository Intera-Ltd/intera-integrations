# Security rules

This repository is public and the catalog is shared by every INTERA
installation. These rules are not negotiable; CI enforces some of them and
reviewers enforce the rest. To report a vulnerability, see
[SECURITY.md](../SECURITY.md).

## Images only by digest

- `deployment.image` must be `<registry>/<repository>@sha256:<64 hex>`. Tags
  (`:latest`, `:1.0.0`) are rejected by the catalog: a tag can be moved, a
  digest cannot.
- The digest is written by the release workflow from the image it has just
  built and pushed. Do not paste digests of images built elsewhere.
- Prefer official, minimal base images and keep them current. Pinning base
  images by digest in the `Dockerfile` is welcome.

## No secrets in the manifest, code or images

- `manifest.yaml` is public and stored in the catalog forever. Never put
  secrets in `deployment.env` or any other field. The validator rejects
  `env` names that look like secrets (`*_TOKEN`, `*_PASSWORD`, `*API_KEY*`,
  ...), but you are responsible for the values.
- Secrets an integration needs are `secret` fields of the connection
  (`configSchema`). Core stores them encrypted and passes them to the bundle
  only when it calls it. Administrator-set container secrets
  (`deployment.settings`) are coming in the next platform release.
- No credentials in source, tests, fixtures, Dockerfiles, build arguments or
  image layers. Tests use obviously fake values.
- Never log secrets, full request headers or raw upstream responses, and do
  not return them in error messages.

## No internal details

Do not commit internal hostnames, IP addresses, customer names, links to
private repositories or names of people. Use `example.com`, `example.test`
and placeholders.

## Bundles

- No `eval`, `new Function`, dynamic `require` of downloaded code, child
  processes or writes to the file system.
- Network access only to the upstream API the administrator configured or to
  your own service at `intera_service_url`. No telemetry or "phone home".
- Validate URLs from configuration (`http:`/`https:` only) and every field
  of an upstream answer before using it.
- Keep dependencies at zero at runtime; dev dependencies come from npm with a
  committed `package-lock.json`.

## Services

- Run as a non-root user; do not require extra capabilities or privileged
  mode.
- Expose only `deployment.port`; serve `deployment.health_path` without
  sensitive data.
- Treat input from the bundle as untrusted; the network boundary is not
  authentication.

## CI

Workflows use only the built-in `GITHUB_TOKEN` with the smallest
permissions. Pull request workflows have read-only access and never get
secrets, so code from forks cannot publish anything. Releases run only from
tags pushed by maintainers or from a manual run.
