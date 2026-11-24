import {
  PRODUCTS_QUERY_API_PATH,
  PURCHASE_ORDER_LINES_QUERY_API_PATH,
  SALES_ORDER_LINES_QUERY_API_PATH,
  SALES_SHIPMENT_LINES_QUERY_API_PATH,
  STOCK_COUNT_LINES_QUERY_API_PATH,
  STOCK_ON_HAND_QUERY_API_PATH,
  STOCK_TRANSFER_LINES_QUERY_API_PATH,
  SUPPLIER_RETURN_LINES_QUERY_API_PATH,
  WAREHOUSES_QUERY_API_PATH,
  createUnleashedClient,
} from '../api/unleashed-client';
import { readUnleashedConfig } from '../config';
import {
  PRODUCTS_SOURCE_ID,
  PRODUCTS_SOURCE_NAME,
  PURCHASE_ORDER_LINES_SOURCE_ID,
  PURCHASE_ORDER_LINES_SOURCE_NAME,
  SALES_ORDER_LINES_SOURCE_ID,
  SALES_ORDER_LINES_SOURCE_NAME,
  SALES_SHIPMENT_LINES_SOURCE_ID,
  SALES_SHIPMENT_LINES_SOURCE_NAME,
  STOCK_COUNT_LINES_SOURCE_ID,
  STOCK_COUNT_LINES_SOURCE_NAME,
  STOCK_ON_HAND_SOURCE_ID,
  STOCK_ON_HAND_SOURCE_NAME,
  STOCK_TRANSFER_LINES_SOURCE_ID,
  STOCK_TRANSFER_LINES_SOURCE_NAME,
  SUPPLIER_RETURN_LINES_SOURCE_ID,
  SUPPLIER_RETURN_LINES_SOURCE_NAME,
  WAREHOUSES_SOURCE_ID,
  WAREHOUSES_SOURCE_NAME,
} from '../constants';
import type { IDataQuery, IDataQueryContext, IDataQueryExecutor } from '../types';
import {
  PRODUCT_FIELDS,
  PURCHASE_ORDER_LINE_FIELDS,
  SALES_ORDER_LINE_FIELDS,
  SALES_SHIPMENT_LINE_FIELDS,
  STOCK_COUNT_LINE_FIELDS,
  STOCK_ON_HAND_FIELDS,
  STOCK_TRANSFER_LINE_FIELDS,
  SUPPLIER_RETURN_LINE_FIELDS,
  WAREHOUSE_FIELDS,
  normalizeDataQuery,
} from './unleashed-data-sources.source';

function createUnleashedDataQueryExecutor(
  key: string,
  label: string,
  fields: IDataQueryExecutor['fields'],
  apiPath: string,
): IDataQueryExecutor {
  return {
    key,
    label,
    fields,
    async execute(context: IDataQueryContext, query: IDataQuery): Promise<Record<string, unknown>> {
      const client = createUnleashedClient(readUnleashedConfig(context));
      return client.queryObject(apiPath, normalizeDataQuery(query), label, context?.signal);
    },
  };
}

export const warehousesExecutor: IDataQueryExecutor = createUnleashedDataQueryExecutor(
  WAREHOUSES_SOURCE_ID,
  WAREHOUSES_SOURCE_NAME,
  WAREHOUSE_FIELDS,
  WAREHOUSES_QUERY_API_PATH,
);

export const productsExecutor: IDataQueryExecutor = createUnleashedDataQueryExecutor(
  PRODUCTS_SOURCE_ID,
  PRODUCTS_SOURCE_NAME,
  PRODUCT_FIELDS,
  PRODUCTS_QUERY_API_PATH,
);

export const stockOnHandExecutor: IDataQueryExecutor = createUnleashedDataQueryExecutor(
  STOCK_ON_HAND_SOURCE_ID,
  STOCK_ON_HAND_SOURCE_NAME,
  STOCK_ON_HAND_FIELDS,
  STOCK_ON_HAND_QUERY_API_PATH,
);

export const purchaseOrderLinesExecutor: IDataQueryExecutor = createUnleashedDataQueryExecutor(
  PURCHASE_ORDER_LINES_SOURCE_ID,
  PURCHASE_ORDER_LINES_SOURCE_NAME,
  PURCHASE_ORDER_LINE_FIELDS,
  PURCHASE_ORDER_LINES_QUERY_API_PATH,
);

export const salesOrderLinesExecutor: IDataQueryExecutor = createUnleashedDataQueryExecutor(
  SALES_ORDER_LINES_SOURCE_ID,
  SALES_ORDER_LINES_SOURCE_NAME,
  SALES_ORDER_LINE_FIELDS,
  SALES_ORDER_LINES_QUERY_API_PATH,
);

export const salesShipmentLinesExecutor: IDataQueryExecutor = createUnleashedDataQueryExecutor(
  SALES_SHIPMENT_LINES_SOURCE_ID,
  SALES_SHIPMENT_LINES_SOURCE_NAME,
  SALES_SHIPMENT_LINE_FIELDS,
  SALES_SHIPMENT_LINES_QUERY_API_PATH,
);

export const stockTransferLinesExecutor: IDataQueryExecutor = createUnleashedDataQueryExecutor(
  STOCK_TRANSFER_LINES_SOURCE_ID,
  STOCK_TRANSFER_LINES_SOURCE_NAME,
  STOCK_TRANSFER_LINE_FIELDS,
  STOCK_TRANSFER_LINES_QUERY_API_PATH,
);

export const stockCountLinesExecutor: IDataQueryExecutor = createUnleashedDataQueryExecutor(
  STOCK_COUNT_LINES_SOURCE_ID,
  STOCK_COUNT_LINES_SOURCE_NAME,
  STOCK_COUNT_LINE_FIELDS,
  STOCK_COUNT_LINES_QUERY_API_PATH,
);

export const supplierReturnLinesExecutor: IDataQueryExecutor = createUnleashedDataQueryExecutor(
  SUPPLIER_RETURN_LINES_SOURCE_ID,
  SUPPLIER_RETURN_LINES_SOURCE_NAME,
  SUPPLIER_RETURN_LINE_FIELDS,
  SUPPLIER_RETURN_LINES_QUERY_API_PATH,
);

export const unleashedDataQueryExecutors: IDataQueryExecutor[] = [
  warehousesExecutor,
  productsExecutor,
  stockOnHandExecutor,
  purchaseOrderLinesExecutor,
  salesOrderLinesExecutor,
  salesShipmentLinesExecutor,
  stockTransferLinesExecutor,
  stockCountLinesExecutor,
  supplierReturnLinesExecutor,
];
