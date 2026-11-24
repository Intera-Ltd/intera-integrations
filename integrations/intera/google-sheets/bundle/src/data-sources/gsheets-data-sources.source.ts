import {
  SHEET_ROWS_SOURCE_DESCRIPTION,
  SHEET_ROWS_SOURCE_ID,
  SHEET_ROWS_SOURCE_NAME,
} from '../constants';
import { readGsheetConfig } from '../config';
import { readTable } from '../sheets/sheet-reader';
import { sheetIdOfSource } from '../sheets/tab-source-id';
import type {
  IDataSource,
  IDataSourceContext,
  IDataSourcePage,
  IDataSourceParams,
} from '../types';
import {
  applyOrder,
  buildEvaluationSchema,
  evaluateExpressions,
  matchesFilter,
} from './query-evaluator';
import { analyzeQuery } from './query-shape';

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
    const shape = analyzeQuery(params, config.weekStart, evaluationSchema.now);

    let filtered = rows;
    for (const filter of shape.filters) {
      filtered = filtered.filter((row) =>
        matchesFilter(row, filter, evaluationSchema),
      );
    }

    const shaped =
      Array.isArray(params.expressions) && params.expressions.length > 0
        ? evaluateExpressions(
            filtered,
            params.expressions,
            evaluationSchema,
            params.orderBy,
            Array.isArray(params.groupBy) ? params.groupBy : undefined,
          )
        : applyOrder(filtered, params.orderBy, evaluationSchema);

    if (typeof params.offset === 'number' && params.offset >= 0) {
      const limit =
        typeof params.limit === 'number' && params.limit > 0
          ? params.limit
          : undefined;
      const start = params.offset;
      const end = limit === undefined ? undefined : start + limit;
      return { rows: shaped.slice(start, end), total: shaped.length };
    }

    if (typeof params.limit === 'number' && params.limit > 0) {
      return shaped.slice(0, params.limit);
    }
    return shaped;
  },
};

export const gsheetsDataSources: IDataSource[] = [gsheetRowsDataSource];
