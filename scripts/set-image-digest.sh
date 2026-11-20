#!/usr/bin/env bash
# Writes an image reference pinned by digest into deployment.image of a
# manifest.yaml.
#
# Usage:
#   scripts/set-image-digest.sh <manifest.yaml> <image> [--out <file>]
#
# <image> is either
#   - a reference with a digest:  ghcr.io/acme/billing@sha256:<64 hex>
#   - a pushed image tag:         ghcr.io/acme/billing:1.0.0
#     (resolved through the registry with `docker buildx imagetools inspect`,
#     which gives the digest of the multi-platform index; falls back to the
#     digest Docker recorded when it pushed the image)
#
# Without --out the manifest is changed in place. Only the `image:` line under
# `deployment:` is rewritten; comments and the rest of the file stay as they are.
set -euo pipefail

usage() { echo "usage: $0 <manifest.yaml> <image> [--out <file>]" >&2; exit 2; }
[[ $# -ge 2 ]] || usage
manifest=$1; image=$2; shift 2
out=$manifest
if [[ $# -gt 0 ]]; then
  [[ $1 == --out && $# -eq 2 ]] || usage
  out=$2
fi
[[ -f $manifest ]] || { echo "error: $manifest not found" >&2; exit 1; }

digest_re='^[a-z0-9]+([._-][a-z0-9]+)*(:[0-9]+)?(/[a-z0-9]+([._-][a-z0-9]+)*)*@sha256:[a-f0-9]{64}$'
if [[ ! $image =~ $digest_re ]]; then
  digest=$(docker buildx imagetools inspect "$image" --format '{{json .Manifest.Digest}}' 2>/dev/null | tr -d '"' || true)
  if [[ ! $digest =~ ^sha256:[a-f0-9]{64}$ ]]; then
    # Fallback for registries buildx cannot reach: the digest Docker recorded on push.
    digest=$(docker image inspect "$image" --format '{{range .RepoDigests}}{{println .}}{{end}}' 2>/dev/null \
      | sed -n 's/.*@\(sha256:[a-f0-9]\{64\}\)$/\1/p' | head -1 || true)
  fi
  if [[ ! $digest =~ ^sha256:[a-f0-9]{64}$ ]]; then
    echo "error: could not resolve a digest for $image (is it pushed?)" >&2
    exit 1
  fi
  # Strip the tag, keep a registry port such as localhost:5005/name:tag.
  last=${image##*/}
  if [[ $last == *:* ]]; then repo="${image%:*}"; else repo=$image; fi
  image="${repo}@${digest}"
fi
if [[ ! $image =~ $digest_re ]]; then
  echo "error: $image is not a lowercase image reference pinned by digest" >&2
  exit 1
fi

tmp=$(mktemp)
awk -v image="$image" '
  /^deployment:[[:space:]]*(#.*)?$/ { in_deployment = 1; print; next }
  in_deployment && /^[^[:space:]#]/ { in_deployment = 0 }
  in_deployment && /^[[:space:]]+image:/ && !done {
    match($0, /^[[:space:]]+/)
    print substr($0, 1, RLENGTH) "image: " image
    done = 1
    next
  }
  { print }
  END { if (!done) exit 3 }
' "$manifest" > "$tmp" || { rm -f "$tmp"; echo "error: no deployment.image line in $manifest" >&2; exit 1; }
mv "$tmp" "$out"
echo "deployment.image = $image ($out)"
