import { githubDataSources } from './data-sources/github-data-sources.source';
import { manifest } from './manifest';
import { testConnection } from './test-connection';
import type { IDataSource } from './types';

export const dataSources: IDataSource[] = githubDataSources;

export function getDataSources(): IDataSource[] {
  return githubDataSources;
}

export { manifest, testConnection };
