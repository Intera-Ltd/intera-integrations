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
import type {
  IDataQuery,
  IDataSource,
  IDataSourceContext,
  IDataSourceFieldDescriptor,
  IDataSourceParams,
} from '../types';

/**
 * Field descriptors, one list per data source. Every name must exist in the matching allowlist in
 * `Intera.Unleashed.Api/Models/Query/UnleashedQueryFields.cs` — the engine rejects a field it does
 * not know, so a rename has to happen on both sides at once.
 *
 * Six of the nine sources are line-grained: one row is one document line, with the document's own
 * number, date, status, warehouse and counterparty repeated on it. The document's monetary totals
 * are deliberately absent, because at this grain summing them would multiply each document by its
 * line count.
 */

export const WAREHOUSE_FIELDS: IDataSourceFieldDescriptor[] = [
  { name: 'warehouse_id', type: 'string', label: 'Warehouse ID' },
  { name: 'warehouse_code', type: 'string', label: 'Warehouse Code' },
  { name: 'warehouse_name', type: 'string', label: 'Warehouse Name' },
  { name: 'is_default', type: 'boolean', label: 'Default Warehouse' },
  { name: 'is_obsolete', type: 'boolean', label: 'Obsolete' },
  { name: 'street_no', type: 'string', label: 'Street No' },
  { name: 'address_line1', type: 'string', label: 'Address Line 1' },
  { name: 'address_line2', type: 'string', label: 'Address Line 2' },
  { name: 'city', type: 'string', label: 'City' },
  { name: 'region', type: 'string', label: 'Region' },
  { name: 'country', type: 'string', label: 'Country' },
  { name: 'post_code', type: 'string', label: 'Post Code' },
  { name: 'phone_number', type: 'string', label: 'Phone Number' },
  { name: 'fax_number', type: 'string', label: 'Fax Number' },
  { name: 'mobile_number', type: 'string', label: 'Mobile Number' },
  { name: 'ddi_number', type: 'string', label: 'DDI Number' },
  { name: 'contact_name', type: 'string', label: 'Contact Name' },
  { name: 'last_modified_on', type: 'date', label: 'Last Modified On' },
];

export const PRODUCT_FIELDS: IDataSourceFieldDescriptor[] = [
  { name: 'product_id', type: 'string', label: 'Product ID' },
  { name: 'product_code', type: 'string', label: 'Product Code' },
  { name: 'product_description', type: 'string', label: 'Product Description' },
  { name: 'barcode', type: 'string', label: 'Barcode' },
  { name: 'product_group', type: 'string', label: 'Product Group' },
  { name: 'product_sub_group', type: 'string', label: 'Product Sub Group' },
  { name: 'product_brand', type: 'string', label: 'Brand' },
  { name: 'unit_of_measure', type: 'string', label: 'Unit of Measure' },
  { name: 'purchase_unit_of_measure', type: 'string', label: 'Purchase Unit of Measure' },
  { name: 'supplier_code', type: 'string', label: 'Supplier Code' },
  { name: 'supplier_name', type: 'string', label: 'Supplier Name' },
  { name: 'pack_size', type: 'number', label: 'Pack Size' },
  { name: 'width', type: 'number', label: 'Width' },
  { name: 'height', type: 'number', label: 'Height' },
  { name: 'depth', type: 'number', label: 'Depth' },
  { name: 'weight', type: 'number', label: 'Weight' },
  { name: 'min_stock_alert_level', type: 'number', label: 'Min Stock Alert Level' },
  { name: 'max_stock_alert_level', type: 'number', label: 'Max Stock Alert Level' },
  { name: 'last_cost', type: 'number', label: 'Last Cost' },
  { name: 'default_purchase_price', type: 'number', label: 'Default Purchase Price' },
  { name: 'default_sell_price', type: 'number', label: 'Default Sell Price' },
  { name: 'average_land_price', type: 'number', label: 'Average Landed Price' },
  { name: 'nominal_cost', type: 'number', label: 'Nominal Cost' },
  { name: 'minimum_sell_price', type: 'number', label: 'Minimum Sell Price' },
  { name: 'minimum_sale_quantity', type: 'number', label: 'Minimum Sale Quantity' },
  { name: 'minimum_order_quantity', type: 'number', label: 'Minimum Order Quantity' },
  { name: 'is_obsolete', type: 'boolean', label: 'Obsolete' },
  { name: 'never_diminishing', type: 'boolean', label: 'Never Diminishing' },
  { name: 'is_component', type: 'boolean', label: 'Is Component' },
  { name: 'is_assembled_product', type: 'boolean', label: 'Is Assembled Product' },
  { name: 'is_serialized', type: 'boolean', label: 'Is Serialized' },
  { name: 'is_batch_tracked', type: 'boolean', label: 'Is Batch Tracked' },
  { name: 'is_purchasable', type: 'boolean', label: 'Is Purchasable' },
  { name: 'is_sellable', type: 'boolean', label: 'Is Sellable' },
  { name: 'bin_location', type: 'string', label: 'Bin Location' },
  { name: 'notes', type: 'string', label: 'Notes' },
  { name: 'created_on', type: 'date', label: 'Created On' },
  { name: 'created_by', type: 'string', label: 'Created By' },
  { name: 'last_modified_on', type: 'date', label: 'Last Modified On' },
  { name: 'last_modified_by', type: 'string', label: 'Last Modified By' },
];

export const STOCK_ON_HAND_FIELDS: IDataSourceFieldDescriptor[] = [
  { name: 'stock_on_hand_id', type: 'string', label: 'Stock On Hand ID' },
  { name: 'product_id', type: 'string', label: 'Product ID' },
  { name: 'product_code', type: 'string', label: 'Product Code' },
  { name: 'product_description', type: 'string', label: 'Product Description' },
  { name: 'product_group', type: 'string', label: 'Product Group' },
  { name: 'warehouse_id', type: 'string', label: 'Warehouse ID' },
  { name: 'warehouse_code', type: 'string', label: 'Warehouse Code' },
  { name: 'warehouse_name', type: 'string', label: 'Warehouse Name' },
  { name: 'qty_on_hand', type: 'number', label: 'Qty On Hand' },
  { name: 'allocated_qty', type: 'number', label: 'Allocated Qty' },
  { name: 'available_qty', type: 'number', label: 'Available Qty' },
  { name: 'on_purchase', type: 'number', label: 'On Purchase' },
  { name: 'avg_cost', type: 'number', label: 'Average Cost' },
  { name: 'total_cost', type: 'number', label: 'Total Cost' },
  { name: 'days_since_last_sale', type: 'number', label: 'Days Since Last Sale' },
  { name: 'last_modified_on', type: 'date', label: 'Last Modified On' },
];

export const PURCHASE_ORDER_LINE_FIELDS: IDataSourceFieldDescriptor[] = [
  { name: 'order_id', type: 'string', label: 'Order ID' },
  { name: 'order_number', type: 'string', label: 'Order Number' },
  { name: 'order_date', type: 'date', label: 'Order Date' },
  { name: 'completed_date', type: 'date', label: 'Completed Date' },
  { name: 'received_date', type: 'date', label: 'Received Date' },
  { name: 'supplier_invoice_date', type: 'date', label: 'Supplier Invoice Date' },
  { name: 'order_status', type: 'string', label: 'Order Status' },
  { name: 'custom_order_status', type: 'string', label: 'Custom Order Status' },
  { name: 'supplier_code', type: 'string', label: 'Supplier Code' },
  { name: 'supplier_name', type: 'string', label: 'Supplier Name' },
  { name: 'supplier_ref', type: 'string', label: 'Supplier Ref' },
  { name: 'warehouse_code', type: 'string', label: 'Warehouse Code' },
  { name: 'warehouse_name', type: 'string', label: 'Warehouse Name' },
  { name: 'currency_code', type: 'string', label: 'Currency Code' },
  { name: 'exchange_rate', type: 'number', label: 'Exchange Rate' },
  { name: 'order_comments', type: 'string', label: 'Order Comments' },
  { name: 'created_on', type: 'date', label: 'Created On' },
  { name: 'created_by', type: 'string', label: 'Created By' },
  { name: 'last_modified_on', type: 'date', label: 'Last Modified On' },
  { name: 'last_modified_by', type: 'string', label: 'Last Modified By' },
  { name: 'line_id', type: 'string', label: 'Line ID' },
  { name: 'line_number', type: 'number', label: 'Line Number' },
  { name: 'product_id', type: 'string', label: 'Product ID' },
  { name: 'product_code', type: 'string', label: 'Product Code' },
  { name: 'product_description', type: 'string', label: 'Product Description' },
  { name: 'product_group', type: 'string', label: 'Product Group' },
  { name: 'unit_of_measure', type: 'string', label: 'Unit of Measure' },
  { name: 'delivery_date', type: 'date', label: 'Delivery Date' },
  { name: 'order_quantity', type: 'number', label: 'Order Quantity' },
  { name: 'receipt_quantity', type: 'number', label: 'Receipt Quantity' },
  { name: 'outstanding_quantity', type: 'number', label: 'Outstanding Quantity' },
  { name: 'purchase_unit_order_quantity', type: 'number', label: 'Purchase Unit Order Quantity' },
  { name: 'purchase_unit_receipt_quantity', type: 'number', label: 'Purchase Unit Receipt Quantity' },
  { name: 'unit_price', type: 'number', label: 'Unit Price' },
  { name: 'discounted_unit_price', type: 'number', label: 'Discounted Unit Price' },
  { name: 'discount_rate', type: 'number', label: 'Discount Rate' },
  { name: 'line_total', type: 'number', label: 'Line Total' },
  { name: 'line_tax', type: 'number', label: 'Line Tax' },
  { name: 'line_tax_code', type: 'string', label: 'Line Tax Code' },
  { name: 'line_tax_rate', type: 'number', label: 'Line Tax Rate' },
  { name: 'volume', type: 'number', label: 'Volume' },
  { name: 'weight', type: 'number', label: 'Weight' },
  { name: 'line_comments', type: 'string', label: 'Line Comments' },
];

export const SALES_ORDER_LINE_FIELDS: IDataSourceFieldDescriptor[] = [
  { name: 'order_id', type: 'string', label: 'Order ID' },
  { name: 'order_number', type: 'string', label: 'Order Number' },
  { name: 'order_date', type: 'date', label: 'Order Date' },
  { name: 'required_date', type: 'date', label: 'Required Date' },
  { name: 'completed_date', type: 'date', label: 'Completed Date' },
  { name: 'received_date', type: 'date', label: 'Received Date' },
  { name: 'payment_due_date', type: 'date', label: 'Payment Due Date' },
  { name: 'order_status', type: 'string', label: 'Order Status' },
  { name: 'custom_order_status', type: 'string', label: 'Custom Order Status' },
  { name: 'customer_code', type: 'string', label: 'Customer Code' },
  { name: 'customer_name', type: 'string', label: 'Customer Name' },
  { name: 'customer_ref', type: 'string', label: 'Customer Ref' },
  { name: 'warehouse_code', type: 'string', label: 'Warehouse Code' },
  { name: 'warehouse_name', type: 'string', label: 'Warehouse Name' },
  { name: 'currency_code', type: 'string', label: 'Currency Code' },
  { name: 'exchange_rate', type: 'number', label: 'Exchange Rate' },
  { name: 'salesperson_name', type: 'string', label: 'Salesperson Name' },
  { name: 'sales_order_group', type: 'string', label: 'Sales Order Group' },
  { name: 'delivery_method', type: 'string', label: 'Delivery Method' },
  { name: 'delivery_city', type: 'string', label: 'Delivery City' },
  { name: 'delivery_region', type: 'string', label: 'Delivery Region' },
  { name: 'delivery_country', type: 'string', label: 'Delivery Country' },
  { name: 'delivery_post_code', type: 'string', label: 'Delivery Post Code' },
  { name: 'order_comments', type: 'string', label: 'Order Comments' },
  { name: 'created_on', type: 'date', label: 'Created On' },
  { name: 'created_by', type: 'string', label: 'Created By' },
  { name: 'last_modified_on', type: 'date', label: 'Last Modified On' },
  { name: 'last_modified_by', type: 'string', label: 'Last Modified By' },
  { name: 'line_id', type: 'string', label: 'Line ID' },
  { name: 'line_number', type: 'number', label: 'Line Number' },
  { name: 'line_type', type: 'string', label: 'Line Type' },
  { name: 'product_id', type: 'string', label: 'Product ID' },
  { name: 'product_code', type: 'string', label: 'Product Code' },
  { name: 'product_description', type: 'string', label: 'Product Description' },
  { name: 'order_quantity', type: 'number', label: 'Order Quantity' },
  { name: 'unit_price', type: 'number', label: 'Unit Price' },
  { name: 'discount_rate', type: 'number', label: 'Discount Rate' },
  { name: 'discounted_unit_price', type: 'number', label: 'Discounted Unit Price' },
  { name: 'line_total', type: 'number', label: 'Line Total' },
  { name: 'line_tax', type: 'number', label: 'Line Tax' },
  { name: 'line_tax_code', type: 'string', label: 'Line Tax Code' },
  { name: 'unit_cost', type: 'number', label: 'Unit Cost' },
  { name: 'volume', type: 'number', label: 'Volume' },
  { name: 'weight', type: 'number', label: 'Weight' },
  { name: 'line_comments', type: 'string', label: 'Line Comments' },
];

export const SALES_SHIPMENT_LINE_FIELDS: IDataSourceFieldDescriptor[] = [
  { name: 'shipment_id', type: 'string', label: 'Shipment ID' },
  { name: 'shipment_number', type: 'string', label: 'Shipment Number' },
  { name: 'shipment_status', type: 'string', label: 'Shipment Status' },
  { name: 'dispatch_date', type: 'date', label: 'Dispatch Date' },
  { name: 'order_id', type: 'string', label: 'Order ID' },
  { name: 'order_number', type: 'string', label: 'Order Number' },
  { name: 'tracking_number', type: 'string', label: 'Tracking Number' },
  { name: 'shipping_company', type: 'string', label: 'Shipping Company' },
  { name: 'customer_code', type: 'string', label: 'Customer Code' },
  { name: 'customer_name', type: 'string', label: 'Customer Name' },
  { name: 'customer_ref', type: 'string', label: 'Customer Ref' },
  { name: 'warehouse_code', type: 'string', label: 'Warehouse Code' },
  { name: 'warehouse_name', type: 'string', label: 'Warehouse Name' },
  { name: 'delivery_city', type: 'string', label: 'Delivery City' },
  { name: 'delivery_region', type: 'string', label: 'Delivery Region' },
  { name: 'delivery_country', type: 'string', label: 'Delivery Country' },
  { name: 'delivery_post_code', type: 'string', label: 'Delivery Post Code' },
  { name: 'number_of_packages', type: 'number', label: 'Number Of Packages' },
  { name: 'shipment_weight', type: 'string', label: 'Shipment Weight' },
  { name: 'incoterm', type: 'string', label: 'Incoterm' },
  { name: 'shipment_comments', type: 'string', label: 'Shipment Comments' },
  { name: 'created_on', type: 'date', label: 'Created On' },
  { name: 'created_by', type: 'string', label: 'Created By' },
  { name: 'last_modified_on', type: 'date', label: 'Last Modified On' },
  { name: 'last_modified_by', type: 'string', label: 'Last Modified By' },
  { name: 'line_id', type: 'string', label: 'Line ID' },
  { name: 'line_number', type: 'number', label: 'Line Number' },
  { name: 'product_id', type: 'string', label: 'Product ID' },
  { name: 'product_code', type: 'string', label: 'Product Code' },
  { name: 'product_description', type: 'string', label: 'Product Description' },
  { name: 'shipment_qty', type: 'number', label: 'Shipment Qty' },
  { name: 'unit_cost', type: 'number', label: 'Unit Cost' },
  { name: 'unit_cost_at_time_of_dispatch', type: 'number', label: 'Unit Cost At Dispatch' },
  { name: 'commercial_monetary_value', type: 'number', label: 'Commercial Monetary Value' },
  { name: 'sales_order_line_number', type: 'number', label: 'Sales Order Line Number' },
];

export const STOCK_TRANSFER_LINE_FIELDS: IDataSourceFieldDescriptor[] = [
  { name: 'transfer_id', type: 'string', label: 'Transfer ID' },
  { name: 'transfer_order_number', type: 'string', label: 'Transfer Order Number' },
  { name: 'order_date', type: 'date', label: 'Order Date' },
  { name: 'delivery_date', type: 'date', label: 'Delivery Date' },
  { name: 'transfer_status', type: 'string', label: 'Transfer Status' },
  { name: 'source_warehouse_code', type: 'string', label: 'Source Warehouse Code' },
  { name: 'source_warehouse_name', type: 'string', label: 'Source Warehouse Name' },
  { name: 'destination_warehouse_code', type: 'string', label: 'Destination Warehouse Code' },
  { name: 'destination_warehouse_name', type: 'string', label: 'Destination Warehouse Name' },
  { name: 'transfer_comments', type: 'string', label: 'Transfer Comments' },
  { name: 'created_on', type: 'date', label: 'Created On' },
  { name: 'created_by', type: 'string', label: 'Created By' },
  { name: 'last_modified_on', type: 'date', label: 'Last Modified On' },
  { name: 'last_modified_by', type: 'string', label: 'Last Modified By' },
  { name: 'line_id', type: 'string', label: 'Line ID' },
  { name: 'line_number', type: 'number', label: 'Line Number' },
  { name: 'product_id', type: 'string', label: 'Product ID' },
  { name: 'product_code', type: 'string', label: 'Product Code' },
  { name: 'transfer_quantity', type: 'number', label: 'Transfer Quantity' },
  { name: 'available_quantity', type: 'number', label: 'Available Quantity' },
  { name: 'source_stock_on_hand_before_transfer', type: 'number', label: 'Source Stock Before Transfer' },
  { name: 'average_landed_price_at_time_of_transfer', type: 'number', label: 'Avg Landed Price At Transfer' },
  { name: 'receipt_fifo_date', type: 'date', label: 'Receipt FIFO Date' },
  { name: 'line_comments', type: 'string', label: 'Line Comments' },
];

export const STOCK_COUNT_LINE_FIELDS: IDataSourceFieldDescriptor[] = [
  { name: 'stock_count_id', type: 'string', label: 'Stock Count ID' },
  { name: 'stock_take_code', type: 'string', label: 'Stock Take Code' },
  { name: 'stock_take_name', type: 'string', label: 'Stock Take Name' },
  { name: 'description', type: 'string', label: 'Description' },
  { name: 'count_type', type: 'string', label: 'Count Type' },
  { name: 'status', type: 'string', label: 'Status' },
  { name: 'count_date', type: 'date', label: 'Count Date' },
  { name: 'completed_date', type: 'date', label: 'Completed Date' },
  { name: 'warehouse_code', type: 'string', label: 'Warehouse Code' },
  { name: 'warehouse_name', type: 'string', label: 'Warehouse Name' },
  { name: 'last_modified_on', type: 'date', label: 'Last Modified On' },
  { name: 'line_id', type: 'string', label: 'Line ID' },
  { name: 'line_number', type: 'number', label: 'Line Number' },
  { name: 'product_id', type: 'string', label: 'Product ID' },
  { name: 'product_code', type: 'string', label: 'Product Code' },
  { name: 'product_description', type: 'string', label: 'Product Description' },
  { name: 'on_hand_quantity', type: 'number', label: 'On Hand Quantity' },
  { name: 'available_quantity', type: 'number', label: 'Available Quantity' },
  { name: 'count_quantity', type: 'number', label: 'Count Quantity' },
  { name: 'average_land_price_at_time_of_stock_take', type: 'number', label: 'Avg Landed Price At Count' },
  { name: 'expiry_date', type: 'date', label: 'Expiry Date' },
  { name: 'receipt_fifo_date', type: 'date', label: 'Receipt FIFO Date' },
];

export const SUPPLIER_RETURN_LINE_FIELDS: IDataSourceFieldDescriptor[] = [
  { name: 'supplier_return_id', type: 'string', label: 'Supplier Return ID' },
  { name: 'supplier_return_number', type: 'string', label: 'Supplier Return Number' },
  { name: 'status', type: 'string', label: 'Status' },
  { name: 'return_date', type: 'date', label: 'Return Date' },
  { name: 'supplier_code', type: 'string', label: 'Supplier Code' },
  { name: 'supplier_name', type: 'string', label: 'Supplier Name' },
  { name: 'supplier_ref', type: 'string', label: 'Supplier Ref' },
  { name: 'purchase_order_number', type: 'string', label: 'Purchase Order Number' },
  { name: 'warehouse_code', type: 'string', label: 'Warehouse Code' },
  { name: 'warehouse_name', type: 'string', label: 'Warehouse Name' },
  { name: 'currency_code', type: 'string', label: 'Currency Code' },
  { name: 'exchange_rate', type: 'number', label: 'Exchange Rate' },
  { name: 'return_comments', type: 'string', label: 'Return Comments' },
  { name: 'created_on', type: 'date', label: 'Created On' },
  { name: 'created_by', type: 'string', label: 'Created By' },
  { name: 'last_modified_on', type: 'date', label: 'Last Modified On' },
  { name: 'last_modified_by', type: 'string', label: 'Last Modified By' },
  { name: 'line_id', type: 'string', label: 'Line ID' },
  { name: 'line_number', type: 'number', label: 'Line Number' },
  { name: 'product_id', type: 'string', label: 'Product ID' },
  { name: 'product_code', type: 'string', label: 'Product Code' },
  { name: 'product_description', type: 'string', label: 'Product Description' },
  { name: 'order_quantity', type: 'number', label: 'Order Quantity' },
  { name: 'order_price', type: 'number', label: 'Order Price' },
  { name: 'return_quantity', type: 'number', label: 'Return Quantity' },
  { name: 'return_price', type: 'number', label: 'Return Price' },
  { name: 'return_cost', type: 'number', label: 'Return Cost' },
  { name: 'purchase_unit_order_quantity', type: 'number', label: 'Purchase Unit Order Quantity' },
  { name: 'purchase_unit_return_quantity', type: 'number', label: 'Purchase Unit Return Quantity' },
  { name: 'return_reason', type: 'string', label: 'Return Reason' },
  { name: 'return_to_supplier', type: 'boolean', label: 'Return To Supplier' },
  { name: 'tax_rate', type: 'number', label: 'Tax Rate' },
  { name: 'line_tax', type: 'number', label: 'Line Tax' },
  { name: 'average_landed_price_before_return', type: 'number', label: 'Avg Landed Price Before Return' },
  { name: 'average_landed_price_after_return', type: 'number', label: 'Avg Landed Price After Return' },
  { name: 'stock_on_hand_before_return', type: 'number', label: 'Stock On Hand Before Return' },
  { name: 'required_date', type: 'date', label: 'Required Date' },
  { name: 'line_comments', type: 'string', label: 'Line Comments' },
];

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function parseJsonParam(value: unknown, fieldName: string): unknown {
  if (typeof value !== 'string') {
    return value;
  }

  try {
    return JSON.parse(value) as unknown;
  } catch {
    throw new Error(`DataSource param "${fieldName}" must be valid JSON`);
  }
}

function parseJsonArrayParam(value: unknown, fieldName: string): unknown[] {
  const parsed = parseJsonParam(value, fieldName);

  if (parsed === undefined) {
    return [];
  }

  return Array.isArray(parsed) ? parsed : [parsed];
}

// Core's IDataSourceParams sends `expressions` flat (top-level), not wrapped in `result` — `result` is
// only used by the legacy IKpiDataProvider.execute() query shape. Support both. When neither is set,
// `expressions` was explicitly omitted by the caller, meaning "return rows as-is" — omit `result`
// entirely so the API falls back to its own passthrough instead of us guessing a field list.
export function normalizeDataQuery(rawQuery: unknown): IDataQuery {
  if (!isRecord(rawQuery)) {
    throw new Error('DataSource query must be an object');
  }

  const result = parseJsonParam(rawQuery.result, 'result');
  const expressions = parseJsonParam(rawQuery.expressions, 'expressions');
  const filterItems = [
    ...parseJsonArrayParam(rawQuery.filter, 'filter'),
    ...parseJsonArrayParam(rawQuery.filters, 'filters'),
  ];

  // Explicit grouping keys (canonical contract, 2026-08-28). Core sends them flat on `params`,
  // exactly like `limit`/`offset`/`orderBy`, while the DSL carries them inside `result` — so they
  // have to be lifted in here, and they are only meaningful alongside expressions: on their own
  // there is nothing to group. Ten plugins were dropping them silently until 2026-09-03, which is
  // why an explicit groupBy did nothing even after the API learned to bind it.
  const groupBy = parseJsonParam(rawQuery.groupBy, 'groupBy');
  const groupByKeys =
    Array.isArray(groupBy) && groupBy.every((key) => typeof key === 'string')
      ? (groupBy as string[])
      : undefined;

  const baseResult = isRecord(result)
    ? (result as IDataQuery['result'])
    : Array.isArray(expressions)
      ? ({ expressions } as IDataQuery['result'])
      : undefined;

  const normalizedResult =
    baseResult && groupByKeys?.length
      ? ({ ...baseResult, groupBy: groupByKeys } as IDataQuery['result'])
      : baseResult;

  return {
    ...(filterItems.length > 0 ? { filters: filterItems as NonNullable<IDataQuery['filters']> } : {}),
    ...(normalizedResult ? { result: normalizedResult } : {}),
    ...normalizePaging(rawQuery),
  };
}

/**
 * Maps core's paging/ordering params onto the DSL (host task grid-server-paging).
 *
 * `limit` → `top`, `offset` → `skip`, `orderBy` → `order`. `offset` is what makes the request a
 * PAGE: the API answers a query carrying `skip` with an `X-Total-Count` header, which the client
 * turns into `{ rows, total }`. Without it the response stays a bare array and the host pages
 * client-side.
 *
 * Paging happens inside Intera.Unleashed.Api rather than upstream: Unleashed pages documents while
 * these sources answer in lines, so a page number cannot be handed to it. The API therefore filters
 * the snapshot it read and pages the result, which is also what makes `X-Total-Count` exact.
 */
function normalizePaging(rawQuery: Record<string, unknown>): Partial<IDataQuery> {
  const paging: Partial<IDataQuery> = {};

  const top = parseNonNegativeInteger(rawQuery.limit ?? rawQuery.top);
  if (top !== undefined && top > 0) {
    paging.top = top;
  }

  const skip = parseNonNegativeInteger(rawQuery.offset ?? rawQuery.skip);
  if (skip !== undefined) {
    paging.skip = skip;
  }

  const order = normalizeOrder(rawQuery.orderBy ?? rawQuery.order);
  if (order.length > 0) {
    paging.order = order;
  }

  return paging;
}

function parseNonNegativeInteger(value: unknown): number | undefined {
  const parsed = typeof value === 'number' ? value : Number.parseInt(String(value ?? ''), 10);
  return Number.isInteger(parsed) && parsed >= 0 ? parsed : undefined;
}

/**
 * Core sends `[{ field, direction }]`; the DSL wants the same shape. Malformed entries are dropped
 * rather than thrown on — an unordered page is a visible degradation, a 500 on a stray param is not.
 */
function normalizeOrder(value: unknown): NonNullable<IDataQuery['order']> {
  const parsed = typeof value === 'string' ? parseJsonParam(value, 'orderBy') : value;
  if (!Array.isArray(parsed)) {
    return [];
  }

  const order: NonNullable<IDataQuery['order']> = [];
  for (const entry of parsed) {
    if (!isRecord(entry)) {
      continue;
    }
    const field = entry.field;
    if (typeof field !== 'string' || field.length === 0) {
      continue;
    }
    // Direction is optional in the DSL (the API defaults to ascending), so an entry without one
    // is kept rather than dropped — narrowing a caller's order list would silently change the
    // rows a page contains.
    const direction = entry.direction;
    order.push(direction === 'asc' || direction === 'desc' ? { field, direction } : { field });
  }

  return order;
}

function createUnleashedQueryDataSource(
  id: string,
  name: string,
  description: string,
  fields: IDataSourceFieldDescriptor[],
  apiPath: string,
): IDataSource {
  return {
    id,
    name,
    description,
    fields,
    async getData(params: IDataSourceParams, context: IDataSourceContext): Promise<unknown> {
      const config = readUnleashedConfig(context);
      const query = normalizeDataQuery(params);
      const client = createUnleashedClient(config);
      return client.queryRaw(apiPath, query, context?.signal);
    },
  };
}

export const warehousesDataSource: IDataSource = createUnleashedQueryDataSource(
  WAREHOUSES_SOURCE_ID,
  WAREHOUSES_SOURCE_NAME,
  'The warehouse register. `warehouse_code` is the key every stock and movement source here joins on, so this is the list to build a site filter from. Obsolete sites are included and marked with `is_obsolete` — a closed depot still owns its history.',
  WAREHOUSE_FIELDS,
  WAREHOUSES_QUERY_API_PATH,
);

export const productsDataSource: IDataSource = createUnleashedQueryDataSource(
  PRODUCTS_SOURCE_ID,
  PRODUCTS_SOURCE_NAME,
  'SKU master data: grouping, brand, unit of measure, dimensions, stock alert levels and the standing costs and prices. `product_code` is what every movement source joins on. Per-customer prices and the ten sell-price tiers are not exposed.',
  PRODUCT_FIELDS,
  PRODUCTS_QUERY_API_PATH,
);

export const stockOnHandDataSource: IDataSource = createUnleashedQueryDataSource(
  STOCK_ON_HAND_SOURCE_ID,
  STOCK_ON_HAND_SOURCE_NAME,
  'Current stock at **SKU × warehouse** grain — on hand, allocated, available, on purchase, and the cost behind them. Each warehouse is one upstream read, so a filter naming a site (by `warehouse_code` **or** `warehouse_name`) makes the query proportionally cheaper.',
  STOCK_ON_HAND_FIELDS,
  STOCK_ON_HAND_QUERY_API_PATH,
);

export const purchaseOrderLinesDataSource: IDataSource = createUnleashedQueryDataSource(
  PURCHASE_ORDER_LINES_SOURCE_ID,
  PURCHASE_ORDER_LINES_SOURCE_NAME,
  'Incoming stock, one row per purchase order line. Use `outstanding_quantity` rather than `order_quantity - receipt_quantity`: Unleashed sends no receipt quantity at all until a line is receipted, so the subtraction reads empty on exactly the lines still to arrive. The order’s own totals are not repeated on the line — count orders with `countDistinct(order_number)`.',
  PURCHASE_ORDER_LINE_FIELDS,
  PURCHASE_ORDER_LINES_QUERY_API_PATH,
);

export const salesOrderLinesDataSource: IDataSource = createUnleashedQueryDataSource(
  SALES_ORDER_LINES_SOURCE_ID,
  SALES_ORDER_LINES_SOURCE_NAME,
  'Committed outgoing demand, one row per sales order line. `line_type` is `Charge` on a freight or handling line and empty on a stock line, so a demand figure should filter `line_type IS_NULL`. Order totals are not repeated on the line.',
  SALES_ORDER_LINE_FIELDS,
  SALES_ORDER_LINES_QUERY_API_PATH,
);

export const salesShipmentLinesDataSource: IDataSource = createUnleashedQueryDataSource(
  SALES_SHIPMENT_LINES_SOURCE_ID,
  SALES_SHIPMENT_LINES_SOURCE_NAME,
  'Actual dispatches, one row per shipment line, with the originating sales order on it. `unit_cost_at_time_of_dispatch` is the historical cost and `unit_cost` the current one — margin work wants the first.',
  SALES_SHIPMENT_LINE_FIELDS,
  SALES_SHIPMENT_LINES_QUERY_API_PATH,
);

export const stockTransferLinesDataSource: IDataSource = createUnleashedQueryDataSource(
  STOCK_TRANSFER_LINES_SOURCE_ID,
  STOCK_TRANSFER_LINES_SOURCE_NAME,
  'Stock moving between sites, one row per transfer line, naming both `source_warehouse_code` and `destination_warehouse_code`. The only source here whose upstream resource has no date filter, so a window is narrowed through `last_modified_on`.',
  STOCK_TRANSFER_LINE_FIELDS,
  STOCK_TRANSFER_LINES_QUERY_API_PATH,
);

export const stockCountLinesDataSource: IDataSource = createUnleashedQueryDataSource(
  STOCK_COUNT_LINES_SOURCE_ID,
  STOCK_COUNT_LINES_SOURCE_NAME,
  'Physical against system, one row per stock count line: `on_hand_quantity` is what Unleashed believed, `count_quantity` what was found. The variance is `count_quantity - on_hand_quantity` and is deliberately not a column — it stays empty for a line nobody has counted yet, which is the honest answer.',
  STOCK_COUNT_LINE_FIELDS,
  STOCK_COUNT_LINES_QUERY_API_PATH,
);

export const supplierReturnLinesDataSource: IDataSource = createUnleashedQueryDataSource(
  SUPPLIER_RETURN_LINES_SOURCE_ID,
  SUPPLIER_RETURN_LINES_SOURCE_NAME,
  'Stock going back to suppliers, one row per return line, with the originating purchase order and the landed price on either side of the return.',
  SUPPLIER_RETURN_LINE_FIELDS,
  SUPPLIER_RETURN_LINES_QUERY_API_PATH,
);

export const unleashedDataSources: IDataSource[] = [
  warehousesDataSource,
  productsDataSource,
  stockOnHandDataSource,
  purchaseOrderLinesDataSource,
  salesOrderLinesDataSource,
  salesShipmentLinesDataSource,
  stockTransferLinesDataSource,
  stockCountLinesDataSource,
  supplierReturnLinesDataSource,
];
