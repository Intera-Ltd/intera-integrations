declare const module: { exports: unknown };
import { xeroDataQueryExecutors } from './data-sources/xero-data-query-executors.source';
import { xeroDataSources } from './data-sources/xero-data-sources.source';
import { manifest } from './manifest';
import { testConnection } from './test-connection';

module.exports = {
  manifest,
  dataSources: xeroDataSources,
  getDataSources: () => xeroDataSources,
  kpiDataProviders: xeroDataQueryExecutors,
  testConnection,
};
