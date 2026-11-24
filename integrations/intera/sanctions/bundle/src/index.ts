import { sanctionsDataSources } from './data-sources/sanctions-data-sources.source';
import { manifest } from './manifest';
import { testConnection } from './test-connection';
import type { IDataSource } from './types';

export const dataSources: IDataSource[] = sanctionsDataSources;

export function getDataSources(): IDataSource[] {
  return sanctionsDataSources;
}

export { manifest, testConnection };
