export const INTEGRATION_ID = 'intera-unleashed-integration';
export const INTEGRATION_NAME = 'Unleashed';
export const INTEGRATION_VERSION = '3.2.0';

export const DIRECT_API_KIND = 'direct_api';
export const DATA_SOURCE_CAPABILITY = 'datasource';

/**
 * Five-minute default cache TTL: stock levels, orders, and counts move on warehouse timescales, and
 * Unleashed enforces a monthly API call quota tied to the account's subscription plan.
 */
export const DEFAULT_CACHE_TTL_SEC = 300;

export const INTERA_SERVICE_URL_CONFIG_FIELD = 'intera_service_url';
export const INTERA_SERVICE_TOKEN_CONFIG_FIELD = 'intera_service_token';
export const DEFAULT_REQUEST_TIMEOUT_MS = 200_000;
export const MAX_RESPONSE_BYTES = 64 * 1024 * 1024;

export const TOTAL_COUNT_HEADER = 'X-Total-Count';

export const WAREHOUSES_SOURCE_ID = 'unleashed_warehouses';
export const WAREHOUSES_SOURCE_NAME = 'Warehouses';

export const PRODUCTS_SOURCE_ID = 'unleashed_products';
export const PRODUCTS_SOURCE_NAME = 'Products';

export const STOCK_ON_HAND_SOURCE_ID = 'unleashed_stock_on_hand';
export const STOCK_ON_HAND_SOURCE_NAME = 'Stock On Hand';

export const PURCHASE_ORDER_LINES_SOURCE_ID = 'unleashed_purchase_order_lines';
export const PURCHASE_ORDER_LINES_SOURCE_NAME = 'Purchase Order Lines';

export const SALES_ORDER_LINES_SOURCE_ID = 'unleashed_sales_order_lines';
export const SALES_ORDER_LINES_SOURCE_NAME = 'Sales Order Lines';

export const SALES_SHIPMENT_LINES_SOURCE_ID = 'unleashed_sales_shipment_lines';
export const SALES_SHIPMENT_LINES_SOURCE_NAME = 'Sales Shipment Lines';

export const STOCK_TRANSFER_LINES_SOURCE_ID = 'unleashed_stock_transfer_lines';
export const STOCK_TRANSFER_LINES_SOURCE_NAME = 'Warehouse Transfer Lines';

export const STOCK_COUNT_LINES_SOURCE_ID = 'unleashed_stock_count_lines';
export const STOCK_COUNT_LINES_SOURCE_NAME = 'Stock Count Lines';

export const SUPPLIER_RETURN_LINES_SOURCE_ID = 'unleashed_supplier_return_lines';
export const SUPPLIER_RETURN_LINES_SOURCE_NAME = 'Supplier Return Lines';

export const DATA_SOURCE_IDS = [
  WAREHOUSES_SOURCE_ID,
  PRODUCTS_SOURCE_ID,
  STOCK_ON_HAND_SOURCE_ID,
  PURCHASE_ORDER_LINES_SOURCE_ID,
  SALES_ORDER_LINES_SOURCE_ID,
  SALES_SHIPMENT_LINES_SOURCE_ID,
  STOCK_TRANSFER_LINES_SOURCE_ID,
  STOCK_COUNT_LINES_SOURCE_ID,
  SUPPLIER_RETURN_LINES_SOURCE_ID,
] as const;

export const DATA_SOURCE_NAMES = {
  [WAREHOUSES_SOURCE_ID]: WAREHOUSES_SOURCE_NAME,
  [PRODUCTS_SOURCE_ID]: PRODUCTS_SOURCE_NAME,
  [STOCK_ON_HAND_SOURCE_ID]: STOCK_ON_HAND_SOURCE_NAME,
  [PURCHASE_ORDER_LINES_SOURCE_ID]: PURCHASE_ORDER_LINES_SOURCE_NAME,
  [SALES_ORDER_LINES_SOURCE_ID]: SALES_ORDER_LINES_SOURCE_NAME,
  [SALES_SHIPMENT_LINES_SOURCE_ID]: SALES_SHIPMENT_LINES_SOURCE_NAME,
  [STOCK_TRANSFER_LINES_SOURCE_ID]: STOCK_TRANSFER_LINES_SOURCE_NAME,
  [STOCK_COUNT_LINES_SOURCE_ID]: STOCK_COUNT_LINES_SOURCE_NAME,
  [SUPPLIER_RETURN_LINES_SOURCE_ID]: SUPPLIER_RETURN_LINES_SOURCE_NAME,
} as const;
