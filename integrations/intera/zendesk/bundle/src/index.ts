declare const module: { exports: unknown };
import { zendeskDataQueryExecutors } from './data-sources/zendesk-data-query-executors.source';
import { zendeskDataSources } from './data-sources/zendesk-data-sources.source';
import { manifest } from './manifest';
import { onWebhook, registerWebhook } from './oauth-callback';
import { provisionInstance } from './provisioning';
import { testConnection } from './test-connection';

module.exports = {
  manifest,
  dataSources: zendeskDataSources,
  getDataSources: () => zendeskDataSources,
  kpiDataProviders: zendeskDataQueryExecutors,
  provisionInstance,
  testConnection,
  registerWebhook,
  onWebhook,
};
