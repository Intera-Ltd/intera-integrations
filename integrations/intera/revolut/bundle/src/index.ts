declare const module: { exports: unknown };
import { revolutDataQueryExecutors } from './data-sources/revolut-data-query-executors.source';
import { revolutDataSources } from './data-sources/revolut-data-sources.source';
import { manifest } from './manifest';
import { onWebhook, registerWebhook } from './oauth-callback';
import { testConnection } from './test-connection';

module.exports = {
  manifest,
  dataSources: revolutDataSources,
  getDataSources: () => revolutDataSources,
  kpiDataProviders: revolutDataQueryExecutors,
  testConnection,
  registerWebhook,
  onWebhook,
};
