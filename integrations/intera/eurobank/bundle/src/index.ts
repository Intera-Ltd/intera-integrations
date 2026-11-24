declare const module: { exports: unknown };
import { eurobankDataQueryExecutors } from './data-sources/eurobank-data-query-executors.source';
import { eurobankDataSources } from './data-sources/eurobank-data-sources.source';
import { manifest } from './manifest';
import { onWebhook, registerWebhook } from './oauth-callback';
import { testConnection } from './test-connection';

module.exports = {
  manifest,
  dataSources: eurobankDataSources,
  getDataSources: () => eurobankDataSources,
  kpiDataProviders: eurobankDataQueryExecutors,
  testConnection,
  registerWebhook,
  onWebhook,
};
