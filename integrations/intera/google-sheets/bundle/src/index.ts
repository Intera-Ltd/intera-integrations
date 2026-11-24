import { gsheetsDataSources } from './data-sources/gsheets-data-sources.source';
import { manifest } from './manifest';
import { provisionInstance } from './provisioning';
import { testConnection } from './test-connection';
import type { IDataSource } from './types';

export const dataSources: IDataSource[] = gsheetsDataSources;

export function getDataSources(): IDataSource[] {
  return gsheetsDataSources;
}

export { manifest, provisionInstance, testConnection };
