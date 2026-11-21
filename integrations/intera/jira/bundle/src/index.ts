declare const module: { exports: unknown };
import { jiraDataQueryExecutors } from './data-sources/jira-data-query-executors.source';
import { jiraDataSources } from './data-sources/jira-data-sources.source';
import { manifest } from './manifest';
import { provisionInstance } from './provisioning';
import { testConnection } from './test-connection';

module.exports = {
  manifest,
  dataSources: jiraDataSources,
  getDataSources: () => jiraDataSources,
  kpiDataProviders: jiraDataQueryExecutors,
  provisionInstance,
  testConnection,
};
