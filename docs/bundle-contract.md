# Bundle contract

The bundle is the part of an integration that Core runs. This page describes
**Integration Contract 2**, the current one.

The SDK package is not published yet. Each bundle keeps a small local copy of
the contract types it uses in `src/types.ts` (see the templates) and has **no
runtime dependencies**. Core checks the shape of the module when it loads it,
so a mismatch fails at load time with a clear message.

## Module

- `dist/index.js`, **CommonJS**, runs on Node.js 22 inside Core.
- Only `dist/` goes into the artifact. There is no `node_modules`: if you need
  a library at runtime, bundle it into `dist/` with a bundler, or better, keep
  the dependency in your [service](docker-services.md).
- Core loads integrations at startup. Installing or updating one restarts Core;
  there is no hot reload.

Top-level exports Core reads:

| Export | Required | What it is |
| --- | --- | --- |
| `manifest` | yes | Runtime manifest, below |
| `dataSources` | for data | Array of data sources |
| `getDataSources()` | recommended | Returns the same array |
| `testConnection(context)` | no | "Test connection" button in INTERA |

The contract has more extension points (inbound webhooks, integration events,
write-back actions, widgets, messaging transports). They will get their own
templates here; ask in an issue if you need one now.

## Runtime manifest

```ts
export const manifest = {
  id: 'acme_billing',             // = runtime_id in manifest.yaml
  name: 'Acme Billing',
  version: '1.2.0',               // = version in manifest.yaml
  kind: 'readonly_api',           // direct_api | readonly_api | legacy
  capabilities: ['datasource'],
  defaultCacheTtlSec: 60,         // >= 0
  configSchema: [                 // connection settings the administrator fills
    { name: 'base_url', label: 'API base URL', kind: 'string', required: true },
    { name: 'api_key', label: 'API key', kind: 'secret', required: true },
  ],
  compatibility: { integration_contract: 2 },
};
```

- `kind`: `readonly_api` for integrations that only read, `direct_api` when
  they also act on the external system.
- `configSchema[].kind`: `string`, `number`, `boolean`, `enum` (with
  `options`) or `secret`. Secrets are stored encrypted and decrypted only when
  Core calls the bundle.
- Do not declare `intera_service_url`: Core provides it for integrations with
  a Docker deployment.

## Data sources

```ts
interface IDataSource {
  id: string;                     // stable, referenced by metrics and assets
  name: string;
  description?: string;           // supports **bold**, _italic_, `code`, \n
  fields: IFieldDescriptor[];
  getData(params: IDataSourceParams, context: IDataSourceContext): Promise<unknown[]>;
}

interface IFieldDescriptor {
  name: string;
  type: 'string' | 'number' | 'boolean' | 'date' | 'enum';
  label?: string;
  enumValues?: string[];
  identity?: boolean;             // part of the record key
}
```

- **Fields.** Describe every column you return. Fields with `identity: true`
  form the record key; with one identity field a data source can feed both
  metrics and assets. Keep the order of identity fields stable between
  versions.
- **`params`.** May carry `from`, `to`, `limit`, `offset` and query parts such
  as `filter` and `expressions`. When a part is absent, return the rows as
  they are; never throw because an optional part is missing.
- **`context.config`** — the decrypted connection settings.
- **`context.signal`** — set on automatic reads that have a deadline
  (30 seconds by default). **Forward it to every HTTP call**, combined with
  your own timeout, so the request really stops when Core gives up:

  ```ts
  const signal = context.signal
    ? AbortSignal.any([context.signal, AbortSignal.timeout(10_000)])
    : AbortSignal.timeout(10_000);
  await fetch(url, { signal });
  ```

## Test connection

```ts
export async function testConnection(context: {
  config: Record<string, unknown>;
  instanceId: string | null;
  instanceSlug: string | null;
}): Promise<{ detail?: string } | void> {
  // Resolve = success, throw = failure. Make one cheap call.
}
```

The `detail` and error messages are shown to the administrator and may reach
logs: never include secrets or raw upstream responses in them.

## Calling your service

For integrations with a Docker deployment, Core passes the container address
(for example `http://<container>:8080`) in `context.config.intera_service_url`
to data sources and `testConnection`. Read it from there on every call, never
hardcode it, and fail with a clear message when it is missing. See
[`hello-dotnet`](../integrations/intera/hello-dotnet/bundle/src/service-client.ts).
