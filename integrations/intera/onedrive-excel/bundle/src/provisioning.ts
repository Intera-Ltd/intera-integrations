import { SHEET_ROWS_SOURCE_ID, SHEET_ROWS_SOURCE_NAME } from './constants';
import { parseExcelConfig } from './config';
import { readSample } from './sheets/sheet-reader';
import type {
  IIntegrationProvisionContext,
  IIntegrationProvisionResult,
} from './types';

/**
 * Called by Core when an instance of this integration is created or updated,
 * before the instance configuration is persisted.
 *
 * Validates credentials, verifies Graph item access, opens the `.xlsx`
 * workbook, resolves the target worksheet, and samples rows to report the
 * dynamic column list for `excel-rows`.
 */
export async function provisionInstance(
  context: IIntegrationProvisionContext,
): Promise<IIntegrationProvisionResult> {
  const config = parseExcelConfig(context.config);
  const sample = await readSample(config, context.signal);

  return {
    dataSources: [
      {
        id: SHEET_ROWS_SOURCE_ID,
        name: SHEET_ROWS_SOURCE_NAME,
        description:
          `Rows of worksheet "${sample.title}" in "${sample.fileName}". ` +
          `${sample.schema.fields.length} columns, typed from ${sample.sampledRows} ` +
          'sampled rows when this instance was last saved — re-save it after ' +
          'the header row changes.',
        fields: sample.schema.fields,
      },
    ],
  };
}
