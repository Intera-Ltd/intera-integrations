declare const module: { exports: unknown };
import { soft1DataQueryExecutors } from './data-sources/soft1-data-query-executors.source';
import { soft1DataSources } from './data-sources/soft1-data-sources.source';
import { manifest } from './manifest';
import { testConnection } from './test-connection';

module.exports = {
  manifest,
  dataSources: soft1DataSources,
  getDataSources: () => soft1DataSources,
  kpiDataProviders: soft1DataQueryExecutors,
  testConnection,
};
