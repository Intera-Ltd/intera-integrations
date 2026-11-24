declare const module: { exports: unknown };
import { gsheetsDataSources } from './data-sources/gsheets-data-sources.source';
import { manifest } from './manifest';
import { provisionInstance } from './provisioning';
import { testConnection } from './test-connection';

module.exports = {
  manifest,
  dataSources: gsheetsDataSources,
  getDataSources: () => gsheetsDataSources,
  // Called on instance create/update: verifies the credential and tabs, and
  // reports the dynamic datasources and columns exposed by this spreadsheet.
  provisionInstance,
  // Invoked by Core when an admin presses "Test connection" on an instance.
  testConnection,
};
