import { asanaDataSources } from './data-sources/asana-data-sources.source';
import { manifest } from './manifest';
import { testConnection } from './test-connection';
import type { IDataSource } from './types';

export const dataSources: IDataSource[] = asanaDataSources;

export function getDataSources(): IDataSource[] {
  return asanaDataSources;
}

export { manifest, testConnection };
