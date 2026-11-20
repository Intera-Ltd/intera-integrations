/**
 * Minimal local copy of the INTERA integration contract (Integration
 * Contract 2) used by this bundle. The SDK package is not published, so the
 * bundle keeps the few types it needs next to its code and has no runtime
 * dependencies. Core checks the shape when it loads the bundle.
 * See docs/bundle-contract.md in this repository.
 */

export type TIntegrationKind = 'direct_api' | 'readonly_api' | 'legacy';

export interface IIntegrationConfigField {
  name: string;
  label?: string;
  kind: 'string' | 'number' | 'boolean' | 'enum' | 'secret';
  required?: boolean;
  options?: string[];
  defaultValue?: string | number | boolean;
}

export interface IIntegrationManifest {
  id: string;
  name: string;
  version: string;
  kind: TIntegrationKind;
  /** `datasource` for bundles that expose data sources. */
  capabilities: string[];
  defaultCacheTtlSec: number;
  configSchema?: IIntegrationConfigField[];
  compatibility?: { integration_contract: number };
}

export type TDataSourceFieldType =
  | 'string'
  | 'number'
  | 'boolean'
  | 'date'
  | 'enum';

export interface IFieldDescriptor {
  name: string;
  type: TDataSourceFieldType;
  label?: string;
  enumValues?: string[];
  /** Part of the record's identity key (used to map records to assets). */
  identity?: boolean;
}

export interface IDataSourceParams {
  from?: string;
  to?: string;
  limit?: number;
  offset?: number;
  [key: string]: unknown;
}

export interface IDataSourceContext {
  userId: string;
  roles: string[];
  /** Decrypted connection config; holds `intera_service_url` for Docker integrations. */
  config?: Record<string, unknown>;
  /** Set by Core on deadline-bound reads; forward it to every HTTP call. */
  signal?: AbortSignal;
}

export interface IDataSource {
  id: string;
  name: string;
  description?: string;
  fields: IFieldDescriptor[];
  getData(
    params: IDataSourceParams,
    context: IDataSourceContext,
  ): Promise<unknown[]>;
}

export interface IIntegrationTestConnectionContext {
  config: Record<string, unknown>;
  instanceId: string | null;
  instanceSlug: string | null;
}

export interface IIntegrationTestConnectionResult {
  /** Short text shown next to the result; never put secrets here. */
  detail?: string;
}

/** Resolve to report success, throw to report a failed check. */
export type TIntegrationTestConnection = (
  context: IIntegrationTestConnectionContext,
) => Promise<IIntegrationTestConnectionResult | void>;
