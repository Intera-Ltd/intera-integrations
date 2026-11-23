declare const module: { exports: unknown };
import { zohoInventoryDataQueryExecutors } from './data-sources/zoho-inventory-data-query-executors.source';
import { zohoInventoryDataSources } from './data-sources/zoho-inventory-data-sources.source';
import { manifest } from './manifest';
import { provisionInstance } from './provisioning';
import { testConnection } from './test-connection';

module.exports = {
  manifest,
  dataSources: zohoInventoryDataSources,
  getDataSources: () => zohoInventoryDataSources,
  kpiDataProviders: zohoInventoryDataQueryExecutors,
  provisionInstance,
  testConnection,
};
