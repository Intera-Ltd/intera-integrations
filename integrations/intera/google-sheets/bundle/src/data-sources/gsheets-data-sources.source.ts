import {
  buildEvaluationSchema,
  executeInMemoryQuery,
} from '@intera-integrations/shared';
import { readGsheetConfig } from '../config';
import {
  SHEET_ROWS_SOURCE_DESCRIPTION,
  SHEET_ROWS_SOURCE_ID,
  SHEET_ROWS_SOURCE_NAME,
} from '../constants';
import { readTable } from '../sheets/sheet-reader';
import { sheetIdOfSource } from '../sheets/tab-source-id';
import type {
  IDataSource,
  IDataSourceContext,
  IDataSourcePage,
  IDataSourceParams,
} from '../types';

export const gsheetRowsDataSource: IDataSource = {
  id: SHEET_ROWS_SOURCE_ID,
  name: SHEET_ROWS_SOURCE_NAME,
  description: SHEET_ROWS_SOURCE_DESCRIPTION,
  // Dynamic per-instance schema populated by `provisionInstance`.
  fields: [],

  async getData(
    params: IDataSourceParams,
    context: IDataSourceContext,
  ): Promise<unknown[] | IDataSourcePage> {
    const config = readGsheetConfig(context);
    const sheetId = sheetIdOfSource(context.dataSourceId);
    const { rows, schema } = await readTable(config, sheetId, context.signal);
    const evaluationSchema = buildEvaluationSchema(
      schema.fields,
      config.weekStart,
    );
    return executeInMemoryQuery(rows, params, evaluationSchema);
  },
};

export const gsheetsDataSources: IDataSource[] = [gsheetRowsDataSource];
