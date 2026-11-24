/**
 * Shared INTERA Integration Contract 2 and Query Contract 1.5+ types for
 * TypeScript bundles in `intera-integrations`.
 *
 * Bundled into each integration's `dist/index.js` at build time so catalog
 * artifacts stay zero-dependency at runtime.
 */

export type TIntegrationKind = 'direct_api' | 'readonly_api' | 'legacy';

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

export interface IIntegrationWebhookManifest {
  key: string;
  methods: Array<'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'>;
  registration: 'auto' | 'manual';
  secretField?: string;
  invalidatesRegistrationOn?: string[];
}

export interface IWebhookRequest {
  method: string;
  rawQuery: string;
  headers: Record<string, string | string[] | undefined>;
  rawBody: Buffer;
  sourceIp: string;
  receivedAt: Date;
}

export interface IWebhookResponse {
  status: number;
  headers?: Record<string, string | string[]>;
  body?: string | Buffer;
}

export interface IWebhookHandlerContext {
  config: Record<string, unknown>;
  handlerKey: string;
  webhookUrl: string | null;
}

export interface IWebhookRegistrationContext {
  config: Record<string, unknown>;
  handlerKey: string;
  webhookUrl: string;
  publicId: string;
}

export interface IWebhookRegistrationResult {
  ok: boolean;
  secret?: string;
  providerRef?: string;
  message?: string;
}

export interface IIntegrationManifest {
  id: string;
  name: string;
  version: string;
  kind: TIntegrationKind;
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
  webhooks?: IIntegrationWebhookManifest[];
}

export type DataSourceFieldType =
  | 'string'
  | 'number'
  | 'boolean'
  | 'date'
  | 'enum';

export type TDataSourceFieldType = DataSourceFieldType;

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

export type IFieldDescriptor = IDataSourceFieldDescriptor;

export interface IDataSourceContext {
  userId?: string;
  roles?: string[];
  config?: Record<string, unknown>;
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

export type ComparisonFilterOperator = '=' | '!=' | '<' | '<=' | '>' | '>=';
export type ExpressionFilterOperator =
  | ComparisonFilterOperator
  | 'IS_NULL'
  | 'IS_NOT_NULL';
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

export type RelativeDateSnapEdge = 'start' | 'end' | 'whole';

export type RelativeDateOffset = {
  unit: RelativeDateUnit | string;
  amount: number;
};

export type RelativeDateValue = {
  type: 'relativeDate';
  from?: 'now' | 'today';
  unit: RelativeDateUnit | string;
  amount: number;
  snapTo?: RelativeDateUnit | string;
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

export type FieldExpression = {
  type: 'field';
  field: string;
};

export type NowExpression = {
  type: 'now';
  anchor?: 'now' | 'today';
};

export type RowDateDiffExpression = {
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
  | RowDateDiffExpression
  | RowMathExpression;

export type AggregateExpression = {
  type: 'aggregate';
  action: AggregateAction;
  field?: string;
  expression?: RowExpression;
  filter?: FilterNode;
};

export type DateDiffExpression = {
  type: 'dateDiff';
  unit: RelativeDateUnit | string;
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

export type ResultExpression = {
  alias: string;
  expression: Expression;
};

export interface IDataSourceOrder {
  field: string;
  direction?: 'asc' | 'desc';
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
  groupBy?: string[];
  result?: {
    expressions: ResultExpression[];
    groupBy?: string[];
  };
  timeZone?: string;
  weekStart?: number | string;
  [key: string]: unknown;
}

export interface IDataSourcePage {
  rows: unknown[];
  total: number;
}

export type IDataPage = IDataSourcePage;

export interface IDataSource {
  id: string;
  name: string;
  description?: string;
  fields: IDataSourceFieldDescriptor[];
  getData(
    params: IDataSourceParams,
    context: IDataSourceContext,
  ): Promise<unknown>;
}

export interface IEntityProviderContext {
  instanceId: string;
  entityType: string;
  configJson: Record<string, unknown>;
  correlationId: string;
  signal?: AbortSignal;
}

export interface IDataQueryContext {
  instanceId: string;
  configJson: Record<string, unknown>;
  correlationId: string;
  signal?: AbortSignal;
}

export interface IDataQuery {
  top?: number;
  skip?: number;
  timeZone?: string;
  weekStart?: string;
  filter?: FilterNode;
  filters?: FilterNode[];
  order?: Array<{ field: string; direction?: 'asc' | 'desc' }>;
  result?: {
    expressions: ResultExpression[];
    groupBy?: string[];
  };
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

export interface IOAuthStatus {
  configured: boolean;
  has_access_token: boolean;
  has_refresh_token: boolean;
  access_token_expires_at?: string | null;
  updated_at?: string | null;
  redirect_uri?: string;
  redirect_uri_source?: string | null;
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
  webhookUrls?: Array<{ handlerKey: string; url: string | null }>;
}

export interface ITestConnectionResult {
  status?: 'ok' | 'needs_authorization';
  authorizeUrl?: string;
  detail?: string;
}

export type IIntegrationTestConnectionContext = ITestConnectionContext;
export type IIntegrationTestConnectionResult = ITestConnectionResult;

export type TIntegrationTestConnection = (
  context: IIntegrationTestConnectionContext,
) => Promise<IIntegrationTestConnectionResult | void>;

export interface IIntegrationProvisionContext {
  config: Record<string, unknown>;
  instanceId: string | null;
  instanceSlug: string | null;
  signal?: AbortSignal;
}

export interface IProvisionedDataSource {
  id: string;
  template?: string;
  name?: string;
  description?: string;
  fields: IDataSourceFieldDescriptor[];
}

export interface IIntegrationProvisionResult {
  dataSources: IProvisionedDataSource[];
}

export type DataRecord = Record<string, unknown>;
