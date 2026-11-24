/**
 * Local copies of the INTERA Integration Contract 2 & Query Contract 1.5.0 types.
 * Zero runtime dependencies: only `dist/` ships in the catalog artifact.
 */

export type TIntegrationTypeCategory =
  | 'core'
  | 'billing'
  | 'accounting'
  | 'banking'
  | 'sales_crm'
  | 'ticketing'
  | 'networking'
  | 'manufacturing'
  | 'project_management';

export interface IIntegrationConfigField {
  name: string;
  label?: string;
  kind: 'string' | 'number' | 'boolean' | 'enum' | 'secret';
  required?: boolean;
  options?: string[];
  defaultValue?: string | number | boolean;
}

export interface IIntegrationManifest {
  id: string;
  name: string;
  version: string;
  kind: 'direct_api' | 'readonly_api' | 'legacy';
  capabilities: string[];
  defaultCacheTtlSec: number;
  types?: TIntegrationTypeCategory[];
  requires?: { core?: string };
  compatibility?: { integration_contract: number };
  author?: string;
  icon?: string;
  description?: string;
  iconFile?: string;
  configSchema?: IIntegrationConfigField[];
}

export type DataSourceFieldType =
  | 'string'
  | 'number'
  | 'boolean'
  | 'date'
  | 'enum';

export interface IDataSourceFieldDescriptor {
  name: string;
  type: DataSourceFieldType;
  label?: string;
  enumValues?: string[];
  identity?: boolean;
}

export interface IDataSourceContext {
  userId: string;
  roles: string[];
  config?: Record<string, unknown>;
  /**
   * Set when Core reads a datasource that `provisionInstance` added for this
   * instance (`IProvisionedDataSource.template`): its id. Absent when the
   * statically declared datasource itself is read.
   */
  dataSourceId?: string;
  signal?: AbortSignal;
}

export type FilterOperator =
  | '='
  | '!='
  | '<'
  | '<='
  | '>'
  | '>='
  | 'IN'
  | 'NOT_IN'
  | 'LIKE'
  | 'NOT_LIKE'
  | 'IS_NULL'
  | 'IS_NOT_NULL';

export type LogicalOperator = 'AND' | 'OR' | 'NOT';

export type ComparisonFilterOperator = '=' | '!=' | '<' | '<=' | '>' | '>=';
export type ExpressionFilterOperator =
  | ComparisonFilterOperator
  | 'IS_NULL'
  | 'IS_NOT_NULL';

export type RelativeDateSnapEdge = 'start' | 'end';

export interface RelativeDateOffset {
  unit: string;
  amount: number;
}

export type RelativeDateValue = {
  type: 'relativeDate';
  from?: 'now' | 'today';
  unit: string;
  amount: number;
  /** Anchors the shifted instant to the start or end of this unit. */
  snapTo?: string;
  snapEdge?: RelativeDateSnapEdge;
  offset?: RelativeDateOffset;
};

export type FieldRefValue = {
  type: 'field';
  name: string;
};

export type FilterValue =
  | string
  | number
  | boolean
  | null
  | RelativeDateValue
  | FieldRefValue;

export type FilterCondition = {
  type: 'condition';
  field: string;
  operator: FilterOperator;
  value?: FilterValue | Array<string | number | boolean>;
};

export type FilterExpressionCondition = {
  type: 'expression';
  left: RowExpression;
  operator: ExpressionFilterOperator;
  right?: RowExpression;
};

export type FilterGroup = {
  type: 'group';
  operator: LogicalOperator;
  items: FilterNode[];
};

export type FilterNode =
  | FilterGroup
  | FilterCondition
  | FilterExpressionCondition;

export type AggregateAction =
  | 'sum'
  | 'avg'
  | 'min'
  | 'max'
  | 'count'
  | 'countDistinct'
  | 'exists';

export type MathOperator = '+' | '-' | '*' | '/';

export type ValueExpression = {
  type: 'value';
  value: string | number | boolean | null;
};
export type FieldExpression = { type: 'field'; field: string };
export type NowExpression = { type: 'now'; anchor?: 'now' | 'today' };
export type RowDateDiffExpression = {
  type: 'dateDiff';
  unit: string;
  start: RowExpression;
  end: RowExpression;
};
export type RowMathExpression = {
  type: 'math';
  operator: MathOperator;
  left: RowExpression;
  right: RowExpression;
};
/** Evaluated once per row; never contains an aggregate. */
export type RowExpression =
  | ValueExpression
  | FieldExpression
  | NowExpression
  | RowDateDiffExpression
  | RowMathExpression;

export type AggregateExpression = {
  type: 'aggregate';
  action: AggregateAction;
  /** Mutually exclusive with `expression`. */
  field?: string;
  /** A per-row computation aggregated instead of a single column. */
  expression?: RowExpression;
  filter?: FilterNode;
};
export type DateDiffExpression = {
  type: 'dateDiff';
  unit: string;
  start: Expression;
  end: Expression;
};
export type MathExpression = {
  type: 'math';
  operator: MathOperator;
  left: Expression;
  right: Expression;
};
export type Expression =
  | ValueExpression
  | FieldExpression
  | NowExpression
  | DateDiffExpression
  | AggregateExpression
  | MathExpression;

export type ResultExpression = { alias: string; expression: Expression };

export interface IDataSourceOrder {
  field: string;
  direction: 'asc' | 'desc';
}

export interface IDataSourceParams {
  from?: string;
  to?: string;
  limit?: number;
  offset?: number;
  orderBy?: IDataSourceOrder[];
  filter?: FilterNode;
  filters?: FilterNode[];
  expressions?: ResultExpression[];
  /** Explicit grouping keys; when absent, keys are inferred from non-aggregated fields. */
  groupBy?: string[];
  [key: string]: unknown;
}

export interface IDataSourcePage {
  rows: unknown[];
  total: number;
}

export interface IDataSource {
  id: string;
  name: string;
  description?: string;
  fields: IDataSourceFieldDescriptor[];
  getData(
    params: IDataSourceParams,
    context: IDataSourceContext,
  ): Promise<unknown[] | IDataSourcePage>;
}

export interface IIntegrationProvisionContext {
  config: Record<string, unknown>;
  instanceId: string | null;
  instanceSlug: string | null;
  signal?: AbortSignal;
}

export interface IProvisionedDataSource {
  id: string;
  /** Statically declared template datasource whose `getData` serves added tab datasources. */
  template?: string;
  name?: string;
  description?: string;
  fields: IDataSourceFieldDescriptor[];
}

export interface IIntegrationProvisionResult {
  dataSources: IProvisionedDataSource[];
}

export interface IIntegrationTestConnectionContext {
  config: Record<string, unknown>;
  instanceId: string | null;
  instanceSlug: string | null;
  signal?: AbortSignal;
}

export interface IIntegrationTestConnectionResult {
  /** Short, secret-free summary of the verified target. */
  detail?: string;
}

/** Resolved instance config. */
export interface IGsheetConfig {
  /** Spreadsheet id, extracted from a pasted URL when needed. */
  spreadsheetId: string;
  /** Configured tab name, or null when chosen by {@link sheetGid} or position. */
  sheetName: string | null;
  /** `gid` from a pasted spreadsheet URL, used when `sheet_name` is empty. */
  sheetGid: number | null;
  credentials: IServiceAccount;
  isFirstRowHeader: boolean;
  /** Rows to ignore above the table. */
  skipRows: number;
  identityColumn: string | null;
  /** First day of the week (0 = Sunday … 6 = Saturday) for `snapTo: 'week'`. */
  weekStart: number;
}

/** Service-account credentials used to sign JWT bearer assertions. */
export interface IServiceAccount {
  clientEmail: string;
  /** PEM-encoded RSA private key. */
  privateKey: string;
  /** OAuth 2.0 token endpoint URL from the key file. */
  tokenUri: string;
}

/** One sheet row, keyed by normalized column name. */
export type SheetRecord = Record<string, unknown>;
export type DataRecord = SheetRecord;
