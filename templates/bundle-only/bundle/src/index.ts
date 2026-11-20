/**
 * Entry point Core loads from `dist/index.js` (CommonJS). Core reads the
 * top-level exports: `manifest`, `dataSources`, and the optional hooks such
 * as `testConnection`.
 */
import { recordsDataSource } from './records.datasource';
import { manifest } from './manifest';
import { testConnection } from './test-connection';
import type { IDataSource } from './types';

export const dataSources: IDataSource[] = [recordsDataSource];

export function getDataSources(): IDataSource[] {
  return dataSources;
}

export { manifest, testConnection };
