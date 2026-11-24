declare const module: { exports: unknown };
import { teamCityDataQueryExecutors } from './data-sources/teamcity-data-query-executors.source';
import { teamCityDataSources } from './data-sources/teamcity-data-sources.source';
import { manifest } from './manifest';
import { testConnection } from './test-connection';

module.exports = {
  manifest,
  dataSources: teamCityDataSources,
  getDataSources: () => teamCityDataSources,
  kpiDataProviders: teamCityDataQueryExecutors,
  testConnection,
};
