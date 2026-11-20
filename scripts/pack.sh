#!/usr/bin/env bash
# Builds the bundle of one integration and packs the catalog artifact:
# a .tar.gz with manifest.yaml at the root and the bundle under dist/.
#
# The main way to upload a version is the catalog CLI: `intera-library publish
# <plugin-dir>` (or `intera-library pack <plugin-dir>` to only build the
# archive). Its plugin folder is manifest.yaml with the real image digest plus
# the built dist/ - which is what --stage-dir prepares from this repository's
# layout. Without --stage-dir this script packs the archive itself, as CI does.
#
# Usage:
#   scripts/pack.sh <integration-dir> [--image <image@sha256:...|image:tag>] [--out-dir <dir> | --stage-dir <dir>] [--release]
#
#   --image      write this image into deployment.image of the packed manifest
#                (the manifest in the repository is not changed)
#   --out-dir    where to put the archive (default: out/)
#   --stage-dir  instead of an archive, write the plugin folder (manifest.yaml
#                and dist/) to this directory, for `intera-library publish`;
#                the directory must not exist or be empty
#   --release    refuse to pack a placeholder image digest
#
# Prints the path of the archive (or of the plugin folder) on the last line.
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
usage() { echo "usage: $0 <integration-dir> [--image <ref>] [--out-dir <dir> | --stage-dir <dir>] [--release]" >&2; exit 2; }
[[ $# -ge 1 ]] || usage
dir=$(cd "$1" && pwd); shift
image=""; out_dir="$root/out"; stage_dir=""; release=""
while [[ $# -gt 0 ]]; do
  case $1 in
    --image) [[ $# -ge 2 ]] || usage; image=$2; shift 2 ;;
    --out-dir) [[ $# -ge 2 ]] || usage; out_dir=$2; shift 2 ;;
    --stage-dir) [[ $# -ge 2 ]] || usage; stage_dir=$2; shift 2 ;;
    --release) release="--release"; shift ;;
    *) usage ;;
  esac
done

if [[ -n $stage_dir && -e $stage_dir ]] && [[ -n $(ls -A "$stage_dir") ]]; then
  echo "error: $stage_dir is not empty" >&2; exit 1
fi
[[ -f $dir/manifest.yaml ]] || { echo "error: $dir/manifest.yaml not found" >&2; exit 1; }
[[ -f $dir/bundle/package.json ]] || { echo "error: $dir/bundle/package.json not found" >&2; exit 1; }

field() { sed -nE "s/^$1:[[:space:]]*['\"]?([^'\" #]+)['\"]?.*/\1/p" "$dir/manifest.yaml" | head -1; }
id=$(field id); version=$(field version)
[[ -n $id && -n $version ]] || { echo "error: id or version missing in manifest.yaml" >&2; exit 1; }

echo "Building bundle of $id $version" >&2
(cd "$dir/bundle" && npm ci --no-audit --no-fund >&2 && npm run build >&2)
[[ -f $dir/bundle/dist/index.js ]] || { echo "error: bundle/dist/index.js was not built" >&2; exit 1; }

stage=$(mktemp -d)
trap 'rm -rf "$stage"' EXIT
cp "$dir/manifest.yaml" "$stage/manifest.yaml"
if [[ -n $image ]]; then
  "$root/scripts/set-image-digest.sh" "$stage/manifest.yaml" "$image" >&2
fi
cp -R "$dir/bundle/dist" "$stage/dist"

# Check the manifest that goes into the archive (and the built bundle).
mkdir -p "$stage/check/bundle"
cp "$stage/manifest.yaml" "$stage/check/manifest.yaml"
ln -s "$dir/bundle/dist" "$stage/check/bundle/dist"
node "$root/scripts/validate-manifest.mjs" ${release:+"$release"} "--label=$id $version (packed)" "$stage/check" >&2
rm -rf "$stage/check"

if [[ -n $stage_dir ]]; then
  mkdir -p "$stage_dir"
  stage_dir=$(cd "$stage_dir" && pwd)
  cp "$stage/manifest.yaml" "$stage_dir/manifest.yaml"
  cp -R "$stage/dist" "$stage_dir/dist"
  find "$stage_dir" \( -name '.DS_Store' -o -name '._*' \) -delete
  echo "Plugin folder ready: intera-library publish $stage_dir" >&2
  echo "$stage_dir"
  exit 0
fi

mkdir -p "$out_dir"
out_dir=$(cd "$out_dir" && pwd)
archive="$out_dir/${id//./-}-$version.tar.gz"
# Only manifest.yaml and dist/ at the root; no macOS metadata (._*), owners or
# extended attributes, sorted entries.
(
  cd "$stage"
  find manifest.yaml dist -print | LC_ALL=C sort > "$stage/.files"
  if tar --version 2>/dev/null | grep -q 'GNU tar'; then
    tar --owner=0 --group=0 --numeric-owner --no-recursion -czf "$archive" -T "$stage/.files"
  else
    COPYFILE_DISABLE=1 tar --no-xattrs --uid 0 --gid 0 --no-recursion -czf "$archive" -T "$stage/.files"
  fi
  rm -f "$stage/.files"
)
echo "Packed $(tar -tzf "$archive" | wc -l | tr -d ' ') entries" >&2
echo "$archive"
