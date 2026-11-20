# Security policy

## Reporting a vulnerability

Please **do not** open a public issue, discussion or pull request for a
security problem.

Report it privately through GitHub: open the **Security** tab of this
repository and choose **Report a vulnerability**. Include:

- the affected integration, version and file;
- what an attacker can do and how to reproduce it;
- any proof of concept, with secrets and personal data removed.

We acknowledge reports within 5 business days, keep you informed while we
work on a fix, and credit you in the advisory if you wish.

## Scope

- Integrations, templates, scripts and workflows in this repository.
- Container images released from it (`ghcr.io/intera-ltd/*`) and artifacts
  attached to its releases.

Vulnerabilities in the INTERA platform itself are also welcome through the
same channel; we will route them.

## What we do

- A vulnerable version is deprecated or disabled in the Integration Library.
  A disabled version cannot be installed and installations are told to update
  or remove it.
- A fixed version is released with a new version number; versions are never
  replaced in place.

## Rules for contributors

See [docs/security.md](docs/security.md): images only by digest, no secrets
in manifests, code or images, no internal details in the repository.
