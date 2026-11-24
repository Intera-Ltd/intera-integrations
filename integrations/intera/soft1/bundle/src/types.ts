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
  description?: string;
  configSchema?: IIntegrationConfigField[];
}

export type TIntegrationTypeCategory =
  | 'billing'
  | 'accounting'
  | 'banking'
  | 'sales_crm'
  | 'ticketing'
  | 'networking'
  | 'manufacturing';

export interface IIntegrationConfigField {
  name: string;
  label?: string;
  kind: 'string' | 'number' | 'boolean' | 'enum' | 'secret';
  required?: boolean;
  options?: string[];
  defaultValue?: string | number | boolean;
}

export interface IDataSourceContext {
  userId: string;
  roles: string[];
  config?: Record<string, unknown>;
  signal?: AbortSignal;
}

export interface IDataSourceParams {
  from?: string;
  to?: string;
  limit?: number;
  filter?: unknown;
  expressions?: unknown;
  groupBy?: unknown;
  result?: unknown;
  [key: string]: unknown;
}

export interface IDataSource {
  id: string;
  name: string;
  description?: string;
  fields?: IDataSourceFieldDescriptor[];
  getData(params: IDataSourceParams, context: IDataSourceContext): Promise<unknown>;
}

export interface IEntityProviderContext {
  instanceId: string;
  entityType: string;
  configJson: Record<string, unknown>;
  correlationId: string;
  signal?: AbortSignal;
}

export type DataSourceFieldType = 'string' | 'number' | 'boolean' | 'date' | 'enum';

export interface IDataSourceFieldDescriptor {
  name: string;
  type: DataSourceFieldType;
  label?: string;
  enumValues?: string[];
  identity?: boolean;
  lookup?: {
    datasource: string;
    valueField: string;
    labelField: string;
  };
}

export interface IDataQueryContext {
  instanceId: string;
  configJson: Record<string, unknown>;
  correlationId: string;
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

export type ComparisonFilterOperator = '=' | '!=' | '<' | '<=' | '>' | '>=';
export type ExpressionFilterOperator = ComparisonFilterOperator | 'IS_NULL' | 'IS_NOT_NULL';
export type LogicalOperator = 'AND' | 'OR' | 'NOT';

export type RelativeDateUnit =
  | 'year'
  | 'quarter'
  | 'month'
  | 'week'
  | 'day'
  | 'hour'
  | 'minute'
  | 'second';

export type RelativeDateSnapEdge = 'start' | 'end';

export interface RelativeDateOffset {
  unit: RelativeDateUnit | string;
  amount: number;
}

export type RelativeDateValue = {
  type: 'relativeDate';
  from?: 'now' | 'today';
  unit: RelativeDateUnit | string;
  amount: number;
  snapTo?: RelativeDateUnit | string;
  snapEdge?: RelativeDateSnapEdge;
  offset?: RelativeDateOffset;
};

export interface FieldRefValue {
  type: 'field';
  name: string;
}

export type FilterValue = string | number | boolean | null | RelativeDateValue | FieldRefValue;

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

export type FilterNode = FilterGroup | FilterCondition | FilterExpressionCondition;

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
export type DateDiffExpression = {
  type: 'dateDiff';
  unit: RelativeDateUnit | string;
  start: RowExpression;
  end: RowExpression;
};
export type RowMathExpression = {
  type: 'math';
  operator: MathOperator;
  left: RowExpression;
  right: RowExpression;
};

export type RowExpression =
  | ValueExpression
  | FieldExpression
  | NowExpression
  | DateDiffExpression
  | RowMathExpression;

export type AggregateExpression = {
  type: 'aggregate';
  action: AggregateAction;
  field?: string;
  expression?: RowExpression;
  filter?: FilterNode;
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

export interface IDataQuery {
  top?: number;
  skip?: number;
  filter?: FilterNode;
  filters?: FilterNode[];
  order?: Array<{ field: string; direction?: 'asc' | 'desc' }>;
  result?: {
    expressions: ResultExpression[];
    groupBy?: string[];
  };
}

export interface IDataPage {
  rows: unknown[];
  total: number;
}

export interface IDataQueryExecutor {
  key: string;
  label: string;
  fields: IDataSourceFieldDescriptor[];
  execute(
    context: IDataQueryContext,
    query: IDataQuery,
  ): Promise<Record<string, unknown>>;
}

export interface IPluginConfig {
  serviceUrl: string;
  serviceToken: string;
}

export interface IApiEnvelope<T> {
  ok: boolean;
  data: T | null;
  error: string | null;
}

export interface ITestConnectionContext {
  config: Record<string, unknown>;
  instanceId: string | null;
  instanceSlug: string | null;
  signal?: AbortSignal;
}

export interface ITestConnectionResult {
  detail?: string;
}
