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
  iconFile?: string;
  description?: string;
  configSchema?: IIntegrationConfigField[];
}

export type DataSourceFieldType =
  | 'string'
  | 'number'
  | 'boolean'
  | 'date'
  | 'enum';

export interface IFieldLookupDeclaration {
  datasource: string;
  valueField: string;
  labelField: string;
}

export interface IDataSourceFieldDescriptor {
  name: string;
  type: DataSourceFieldType;
  label?: string;
  enumValues?: string[];
  identity?: boolean;
  lookup?: IFieldLookupDeclaration;
}

export interface IDataSourceContext {
  userId: string;
  roles: string[];
  config?: Record<string, unknown>;
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
export type ExpressionFilterOperator =
  | ComparisonFilterOperator
  | 'IS_NULL'
  | 'IS_NOT_NULL';

export type LogicalOperator = 'AND' | 'OR' | 'NOT';

export type RelativeDateSnapEdge = 'start' | 'end';

export interface RelativeDateOffset {
  unit: string;
  amount: number;
}

export interface RelativeDateValue {
  type: 'relativeDate';
  from?: 'now' | 'today';
  unit: string;
  amount: number;
  snapTo?: string;
  snapEdge?: RelativeDateSnapEdge;
  offset?: RelativeDateOffset;
}

export interface FieldRefValue {
  type: 'field';
  name: string;
}

export type FilterValue =
  | string
  | number
  | boolean
  | null
  | RelativeDateValue
  | FieldRefValue;

export interface FilterCondition {
  type: 'condition';
  field: string;
  operator: FilterOperator;
  value?: FilterValue | Array<string | number | boolean>;
}

export interface FilterExpressionCondition {
  type: 'expression';
  left: RowExpression;
  operator: ExpressionFilterOperator;
  right?: RowExpression;
}

export interface FilterGroup {
  type: 'group';
  operator: LogicalOperator;
  items: FilterNode[];
}

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
export type DateDiffExpression = {
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
  expressions?: ResultExpression[];
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

export interface IIntegrationTestConnectionContext {
  config: Record<string, unknown>;
  instanceId: string | null;
  instanceSlug: string | null;
}

export interface IIntegrationTestConnectionResult {
  detail?: string;
}

export type DataRecord = Record<string, unknown>;
export type AsanaRecord = DataRecord;
export type GitHubRecord = DataRecord;
export type SanctionsRecord = DataRecord;
