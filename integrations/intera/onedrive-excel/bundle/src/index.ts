import { excelDataSources } from './data-sources/excel-data-sources.source';
import { manifest } from './manifest';
import { provisionInstance } from './provisioning';
import { testConnection } from './test-connection';
import type { IDataSource } from './types';

export const dataSources: IDataSource[] = excelDataSources;

export function getDataSources(): IDataSource[] {
  return excelDataSources;
}

export { manifest, provisionInstance, testConnection };
