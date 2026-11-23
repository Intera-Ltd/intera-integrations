declare const module: { exports: unknown };
import { salesforceDataSources } from './data-sources/salesforce-data-sources.source';
import { salesforceDataQueryExecutors } from './data-sources/salesforce-data-query-executors.source';
import { manifest } from './manifest';
import { testConnection } from './test-connection';

module.exports = {
  manifest,
  dataSources: salesforceDataSources,
  getDataSources: () => salesforceDataSources,
  kpiDataProviders: salesforceDataQueryExecutors,
  testConnection,
};
