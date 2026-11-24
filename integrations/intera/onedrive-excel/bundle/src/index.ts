declare const module: { exports: unknown };
import { excelDataSources } from './data-sources/excel-data-sources.source';
import { manifest } from './manifest';
import { provisionInstance } from './provisioning';
import { testConnection } from './test-connection';

module.exports = {
  manifest,
  dataSources: excelDataSources,
  getDataSources: () => excelDataSources,
  // Called on instance create/update: verifies Entra ID / Graph access and
  // reports the worksheet columns exposed by this workbook.
  provisionInstance,
  // Invoked by Core when an admin presses "Test connection" on an instance.
  testConnection,
};
