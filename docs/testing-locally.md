# Testing locally

Everything here runs on your machine with Node.js 20+, npm and Docker. No
INTERA installation is needed.

## 1. Unit tests of the bundle

```bash
cd integrations/<developer>/<integration>/bundle
npm ci
npm test        # tsc build + node --test test/
```

The tests load `dist/index.js` the way Core does and replace the global
`fetch` with a mock, so they never call a real service or API. Cover at least:

- the exports and the runtime manifest (`id`, `version`, data source fields);
- the happy path of each data source;
- missing or invalid configuration (no request is made);
- an upstream error status and a malformed upstream answer;
- that `context.signal` reaches `fetch`;
- that errors never contain secrets.

## 2. The service container

```bash
docker build -t <integration>:dev integrations/<developer>/<integration>/service
docker run --rm -d --name <integration> -p 8080:8080 <integration>:dev
curl -i localhost:8080/health
docker logs <integration>
```

Check that the container runs as a non-root user:
`docker exec <integration> id`.

## 3. The bundle against the container

`scripts/try-bundle.mjs` calls `testConnection` and every data source with
the config you give it, as Core would:

```bash
node scripts/try-bundle.mjs integrations/<developer>/<integration> \
  --service-url http://localhost:8080
# bundle-only integrations take their connection fields instead:
node scripts/try-bundle.mjs integrations/<developer>/<integration> \
  --config base_url=https://api.example.com --config api_key="$API_KEY"
docker rm -f <integration>
```

## 4. Manifest and artifact

```bash
npm ci                                                   # repository tooling
node scripts/validate-manifest.mjs integrations/<developer>/<integration>
scripts/pack.sh integrations/<developer>/<integration>   # -> out/<developer>-<integration>-<version>.tar.gz
tar -tzf out/<developer>-<integration>-<version>.tar.gz
```

The archive must contain only `manifest.yaml` and `dist/`. `pack.sh` takes
care of that (no macOS `._*` files, no extended attributes).

To pack with a real image digest, push the image to a registry you control
and pass it:

```bash
docker tag <integration>:dev <registry>/<integration>:<version>
docker push <registry>/<integration>:<version>
scripts/pack.sh integrations/<developer>/<integration> --image <registry>/<integration>:<version>
```

`--image` also accepts a reference that already has a digest
(`<registry>/<integration>@sha256:...`). The manifest in the repository is not
changed; use `scripts/set-image-digest.sh <manifest.yaml> <image>` to change a
file in place.

## 5. Full check, as CI does

```bash
node scripts/validate-manifest.mjs --all
```
