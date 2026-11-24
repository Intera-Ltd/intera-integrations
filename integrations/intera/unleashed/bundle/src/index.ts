declare const module: { exports: unknown };
import { unleashedDataQueryExecutors } from './data-sources/unleashed-data-query-executors.source';
import { unleashedDataSources } from './data-sources/unleashed-data-sources.source';
import { manifest } from './manifest';
import { testConnection } from './test-connection';

module.exports = {
  manifest,
  dataSources: unleashedDataSources,
  getDataSources: () => unleashedDataSources,
  kpiDataProviders: unleashedDataQueryExecutors,
  testConnection,
};
