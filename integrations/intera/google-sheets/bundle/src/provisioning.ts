import { SHEET_ROWS_SOURCE_ID } from './constants';
import { parseGsheetConfig } from './config';
import { readBookSample, type ISheetSample } from './sheets/sheet-reader';
import { tabSourceId } from './sheets/tab-source-id';
import type {
  IIntegrationProvisionContext,
  IIntegrationProvisionResult,
  IProvisionedDataSource,
} from './types';

/**
 * Called by Core when an instance of this integration is created or updated,
 * before the instance configuration is persisted.
 *
 * Every tabular tab in the spreadsheet becomes a datasource: the primary tab
 * refines the static `gsheet-rows` datasource, and every other grid tab with
 * rows is added on that template as `gsheet-tab-<gid>`.
 */
export async function provisionInstance(
  context: IIntegrationProvisionContext,
): Promise<IIntegrationProvisionResult> {
  const config = parseGsheetConfig(context.config);
  const book = await readBookSample(config, context.signal);

  return {
    dataSources: [
      describe(SHEET_ROWS_SOURCE_ID, book.primary),
      ...book.others.map((sample) => ({
        ...describe(tabSourceId(sample.tab.sheetId), sample),
        template: SHEET_ROWS_SOURCE_ID,
      })),
    ],
  };
}

function describe(id: string, sample: ISheetSample): IProvisionedDataSource {
  return {
    id,
    name: sample.tab.title,
    description:
      `Rows of tab "${sample.tab.title}". ${sample.schema.fields.length} columns, ` +
      `typed from ${sample.sampledRows} sampled rows when this instance was ` +
      'last saved — re-save it after a header row changes or a tab is added.',
    fields: sample.schema.fields,
  };
}
