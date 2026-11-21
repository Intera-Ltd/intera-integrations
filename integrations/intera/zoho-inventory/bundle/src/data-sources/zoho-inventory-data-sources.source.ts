import {
  CUSTOMERS_QUERY_API_PATH,
  ITEMS_QUERY_API_PATH,
  LOCATIONS_QUERY_API_PATH,
  PACKAGES_QUERY_API_PATH,
  PACKAGE_LINES_QUERY_API_PATH,
  PURCHASE_ORDERS_QUERY_API_PATH,
  PURCHASE_ORDER_LINES_QUERY_API_PATH,
  PURCHASE_RECEIVES_QUERY_API_PATH,
  PURCHASE_RECEIVE_LINES_QUERY_API_PATH,
  SALES_ORDERS_QUERY_API_PATH,
  SALES_ORDER_LINES_QUERY_API_PATH,
  SHIPMENTS_QUERY_API_PATH,
  SHIPMENT_LINES_QUERY_API_PATH,
  VENDORS_QUERY_API_PATH,
  CONTACTS_QUERY_API_PATH,
  SALESPERSONS_QUERY_API_PATH,
  INVENTORY_BY_ITEM_QUERY_API_PATH,
  INVENTORY_BY_LOCATION_QUERY_API_PATH,
  INVOICES_QUERY_API_PATH,
  INVOICE_LINES_QUERY_API_PATH,
  BILLS_QUERY_API_PATH,
  BILL_LINES_QUERY_API_PATH,
  createZohoInventoryClient,
} from '../api/zoho-inventory-client';
import { readZohoInventoryConfig } from '../config';
import {
  CUSTOMERS_SOURCE_ID,
  ITEMS_SOURCE_ID,
  LOCATIONS_SOURCE_ID,
  PACKAGES_SOURCE_ID,
  PACKAGE_LINES_SOURCE_ID,
  PURCHASE_ORDERS_SOURCE_ID,
  PURCHASE_ORDER_LINES_SOURCE_ID,
  PURCHASE_RECEIVES_SOURCE_ID,
  PURCHASE_RECEIVE_LINES_SOURCE_ID,
  SALES_ORDERS_SOURCE_ID,
  SALES_ORDER_LINES_SOURCE_ID,
  SHIPMENTS_SOURCE_ID,
  SHIPMENT_LINES_SOURCE_ID,
  VENDORS_SOURCE_ID,
  CONTACTS_SOURCE_ID,
  SALESPERSONS_SOURCE_ID,
  INVENTORY_BY_ITEM_SOURCE_ID,
  INVENTORY_BY_LOCATION_SOURCE_ID,
  INVOICES_SOURCE_ID,
  INVOICE_LINES_SOURCE_ID,
  BILLS_SOURCE_ID,
  BILL_LINES_SOURCE_ID,
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
 * `Intera.ZohoInventory.Api/Models/Query/ZohoQueryFields.cs` — the engine rejects a field it does not
 * know, so a rename has to happen on both sides at once.
 *
 * These are the **static** fields. Custom fields (`cf_…` on a document, `item_cf_…` on a line) differ
 * per organization and are added to these lists by `provisionInstance` when an instance is saved.
 *
 * Every id that points at another source of this integration declares a lookup, so a filter on
 * `customer_id` shows the customer's name next to the raw value. The reference sources are all
 * list-only on the API side, so resolving a label costs one Zoho list read, never a fan-out.
 */

function text(name: string, label: string): IDataSourceFieldDescriptor {
  return { name, type: 'string', label };
}

function num(name: string, label: string): IDataSourceFieldDescriptor {
  return { name, type: 'number', label };
}

function date(name: string, label: string): IDataSourceFieldDescriptor {
  return { name, type: 'date', label };
}

function bool(name: string, label: string): IDataSourceFieldDescriptor {
  return { name, type: 'boolean', label };
}

function ref(
  name: string,
  label: string,
  datasource: string,
  valueField: string,
  labelField: string,
): IDataSourceFieldDescriptor {
  return { name, type: 'string', label, lookup: { datasource, valueField, labelField } };
}

const customerRef = (): IDataSourceFieldDescriptor =>
  ref('customer_id', 'Customer ID', CUSTOMERS_SOURCE_ID, 'customer_id', 'customer_name');
const vendorRef = (name = 'vendor_id', label = 'Vendor ID'): IDataSourceFieldDescriptor =>
  ref(name, label, VENDORS_SOURCE_ID, 'vendor_id', 'vendor_name');
const itemRef = (): IDataSourceFieldDescriptor =>
  ref('item_id', 'Item ID', ITEMS_SOURCE_ID, 'item_id', 'item_name');
const locationRef = (): IDataSourceFieldDescriptor =>
  ref('location_id', 'Location ID', LOCATIONS_SOURCE_ID, 'location_id', 'location_name');
const salesOrderRef = (): IDataSourceFieldDescriptor =>
  ref('salesorder_id', 'Sales Order ID', SALES_ORDERS_SOURCE_ID, 'salesorder_id', 'salesorder_number');
const salespersonRef = (): IDataSourceFieldDescriptor =>
  ref('salesperson_id', 'Salesperson ID', SALESPERSONS_SOURCE_ID, 'salesperson_id', 'salesperson_name');
const purchaseOrderRef = (): IDataSourceFieldDescriptor =>
  ref('purchaseorder_id', 'Purchase Order ID', PURCHASE_ORDERS_SOURCE_ID, 'purchaseorder_id', 'purchaseorder_number');

const CONTACT_FIELDS: IDataSourceFieldDescriptor[] = [
  text('company_name', 'Company Name'),
  text('website', 'Website'),
  text('contact_type', 'Contact Type'),
  text('status', 'Status'),
  text('currency_id', 'Currency ID'),
  text('currency_code', 'Currency'),
  num('payment_terms', 'Payment Terms (days)'),
  text('payment_terms_label', 'Payment Terms'),
  text('first_name', 'First Name'),
  text('last_name', 'Last Name'),
  text('email', 'Email'),
  text('phone', 'Phone'),
  text('mobile', 'Mobile'),
  date('created_time', 'Created Time'),
  date('last_modified_time', 'Last Modified Time'),
];

export const CUSTOMER_FIELDS: IDataSourceFieldDescriptor[] = [
  text('customer_id', 'Customer ID'),
  text('customer_name', 'Customer Name'),
  ...CONTACT_FIELDS,
  num('outstanding_receivable_amount', 'Outstanding Receivable'),
  num('unused_credits_receivable_amount', 'Unused Credits (Receivable)'),
];

export const VENDOR_FIELDS: IDataSourceFieldDescriptor[] = [
  text('vendor_id', 'Vendor ID'),
  text('vendor_name', 'Vendor Name'),
  ...CONTACT_FIELDS,
  num('outstanding_payable_amount', 'Outstanding Payable'),
  num('unused_credits_payable_amount', 'Unused Credits (Payable)'),
];

export const ITEM_FIELDS: IDataSourceFieldDescriptor[] = [
  text('item_id', 'Item ID'),
  text('item_name', 'Item Name'),
  text('sku', 'SKU'),
  text('description', 'Description'),
  text('status', 'Status'),
  text('item_type', 'Item Type'),
  text('product_type', 'Product Type'),
  text('unit', 'Unit'),
  text('group_id', 'Item Group ID'),
  text('group_name', 'Item Group'),
  text('category_id', 'Category ID'),
  text('category_name', 'Category'),
  text('brand', 'Brand'),
  text('manufacturer', 'Manufacturer'),
  num('sales_rate', 'Sales Rate'),
  num('purchase_rate', 'Purchase Rate'),
  text('tax_name', 'Tax'),
  num('tax_percentage', 'Tax %'),
  bool('is_combo_product', 'Composite Item'),
  bool('can_be_sold', 'Can Be Sold'),
  bool('can_be_purchased', 'Can Be Purchased'),
  bool('track_inventory', 'Track Inventory'),
  bool('is_returnable', 'Returnable'),
  text('preferred_vendor_name', 'Preferred Vendor'),
  num('reorder_level', 'Reorder Level'),
  num('stock_on_hand', 'Stock on Hand'),
  num('available_stock', 'Available Stock'),
  num('available_for_sale', 'Available for Sale'),
  num('actual_available_stock', 'Actual Available Stock'),
  text('upc', 'UPC'),
  text('ean', 'EAN'),
  text('isbn', 'ISBN'),
  text('part_number', 'Part Number'),
  date('created_time', 'Created Time'),
  date('last_modified_time', 'Last Modified Time'),
];

export const LOCATION_FIELDS: IDataSourceFieldDescriptor[] = [
  text('location_id', 'Location ID'),
  text('location_name', 'Location Name'),
  text('type', 'Type'),
  text('parent_location_id', 'Parent Location ID'),
  bool('is_primary', 'Primary'),
  bool('is_active', 'Active'),
  text('email', 'Email'),
  text('phone', 'Phone'),
  text('address', 'Address'),
  text('address2', 'Address Line 2'),
  text('city', 'City'),
  text('state', 'State'),
  text('state_code', 'State Code'),
  text('postal_code', 'Postal Code'),
  text('country', 'Country'),
];

export const SALES_ORDER_FIELDS: IDataSourceFieldDescriptor[] = [
  text('salesorder_id', 'Sales Order ID'),
  text('salesorder_number', 'Sales Order Number'),
  customerRef(),
  text('customer_name', 'Customer Name'),
  date('date', 'Order Date'),
  date('shipment_date', 'Expected Shipment Date'),
  date('delivery_date', 'Delivery Date'),
  text('status', 'Status'),
  text('order_status', 'Order Status'),
  text('current_sub_status', 'Sub-status'),
  text('invoiced_status', 'Invoiced Status'),
  text('paid_status', 'Paid Status'),
  text('shipped_status', 'Shipped Status'),
  text('reference_number', 'Reference Number'),
  text('salesperson_name', 'Salesperson'),
  text('delivery_method', 'Delivery Method'),
  text('sales_channel', 'Sales Channel'),
  text('currency_id', 'Currency ID'),
  text('currency_code', 'Currency'),
  num('quantity', 'Quantity Ordered'),
  num('quantity_invoiced', 'Quantity Invoiced'),
  num('quantity_packed', 'Quantity Packed'),
  num('quantity_shipped', 'Quantity Shipped'),
  num('total', 'Total'),
  num('bcy_total', 'Total (Base Currency)'),
  num('total_invoiced_amount', 'Invoiced Amount'),
  num('balance', 'Balance'),
  bool('is_drop_shipment', 'Drop Shipment'),
  bool('is_backorder', 'Backorder'),
  bool('is_emailed', 'Emailed'),
  date('created_time', 'Created Time'),
  date('last_modified_time', 'Last Modified Time'),
];

export const SALES_ORDER_LINE_FIELDS: IDataSourceFieldDescriptor[] = [
  salesOrderRef(),
  text('salesorder_number', 'Sales Order Number'),
  customerRef(),
  text('customer_name', 'Customer Name'),
  date('order_date', 'Order Date'),
  date('shipment_date', 'Expected Shipment Date'),
  text('order_status', 'Order Status'),
  text('reference_number', 'Reference Number'),
  salespersonRef(),
  text('salesperson_name', 'Salesperson'),
  text('currency_code', 'Currency'),
  num('exchange_rate', 'Exchange Rate'),
  text('line_item_id', 'Line Item ID'),
  num('item_order', 'Line Position'),
  itemRef(),
  text('item_name', 'Item Name'),
  text('sku', 'SKU'),
  text('description', 'Description'),
  num('quantity', 'Quantity Ordered'),
  num('quantity_invoiced', 'Quantity Invoiced'),
  num('quantity_packed', 'Quantity Packed'),
  num('quantity_shipped', 'Quantity Shipped'),
  num('quantity_delivered', 'Quantity Delivered'),
  num('quantity_cancelled', 'Quantity Cancelled'),
  num('quantity_returned', 'Quantity Returned'),
  text('unit', 'Unit'),
  num('rate', 'Rate'),
  num('bcy_rate', 'Rate (Base Currency)'),
  text('tax_name', 'Tax'),
  num('tax_percentage', 'Tax %'),
  num('item_total', 'Line Total'),
  bool('is_invoiced', 'Invoiced'),
  bool('is_combo_product', 'Composite Item'),
  text('combo_type', 'Composite Type'),
  locationRef(),
  text('location_name', 'Location Name'),
  date('created_time', 'Order Created Time'),
  date('last_modified_time', 'Order Last Modified Time'),
];

export const PURCHASE_ORDER_FIELDS: IDataSourceFieldDescriptor[] = [
  text('purchaseorder_id', 'Purchase Order ID'),
  text('purchaseorder_number', 'Purchase Order Number'),
  vendorRef(),
  text('vendor_name', 'Vendor Name'),
  date('date', 'Order Date'),
  date('delivery_date', 'Delivery Date'),
  date('expected_delivery_date', 'Expected Delivery Date'),
  text('status', 'Status'),
  text('order_status', 'Order Status'),
  text('received_status', 'Received Status'),
  text('billed_status', 'Billed Status'),
  text('reference_number', 'Reference Number'),
  text('currency_id', 'Currency ID'),
  text('currency_code', 'Currency'),
  num('total', 'Total'),
  num('total_ordered_quantity', 'Quantity Ordered'),
  num('quantity_yet_to_receive', 'Quantity Yet to Receive'),
  num('quantity_marked_as_received', 'Quantity Marked as Received'),
  bool('is_po_marked_as_received', 'Marked as Received'),
  bool('is_drop_shipment', 'Drop Shipment'),
  bool('is_backorder', 'Backorder'),
  date('created_time', 'Created Time'),
  date('last_modified_time', 'Last Modified Time'),
];

export const PURCHASE_ORDER_LINE_FIELDS: IDataSourceFieldDescriptor[] = [
  purchaseOrderRef(),
  text('purchaseorder_number', 'Purchase Order Number'),
  vendorRef(),
  text('vendor_name', 'Vendor Name'),
  date('order_date', 'Order Date'),
  date('delivery_date', 'Delivery Date'),
  date('expected_delivery_date', 'Expected Delivery Date'),
  text('po_status', 'Purchase Order Status'),
  text('reference_number', 'Reference Number'),
  salesOrderRef(),
  text('currency_code', 'Currency'),
  num('exchange_rate', 'Exchange Rate'),
  text('line_item_id', 'Line Item ID'),
  num('item_order', 'Line Position'),
  itemRef(),
  text('item_name', 'Item Name'),
  text('sku', 'SKU'),
  text('description', 'Description'),
  text('salesorder_item_id', 'Sales Order Line Item ID'),
  num('quantity', 'Quantity Ordered'),
  num('quantity_received', 'Quantity Received'),
  num('quantity_billed', 'Quantity Billed'),
  num('quantity_cancelled', 'Quantity Cancelled'),
  text('unit', 'Unit'),
  num('purchase_rate', 'Purchase Rate'),
  num('bcy_rate', 'Rate (Base Currency)'),
  text('tax_name', 'Tax'),
  num('tax_percentage', 'Tax %'),
  num('item_total', 'Line Total'),
  locationRef(),
  text('location_name', 'Location Name'),
  date('created_time', 'Order Created Time'),
  date('last_modified_time', 'Order Last Modified Time'),
];

export const PURCHASE_RECEIVE_FIELDS: IDataSourceFieldDescriptor[] = [
  text('receive_id', 'Receive ID'),
  text('receive_number', 'Receive Number'),
  date('date', 'Receive Date'),
  text('status', 'Status'),
  text('received_status', 'Received Status'),
  text('billed_status', 'Billed Status'),
  purchaseOrderRef(),
  text('purchaseorder_number', 'Purchase Order Number'),
  vendorRef(),
  text('vendor_name', 'Vendor Name'),
  num('quantity_received', 'Quantity Received'),
  locationRef(),
  text('location_name', 'Location Name'),
  bool('is_salesreturn', 'Sales Return Receive'),
  date('created_time', 'Created Time'),
  date('last_modified_time', 'Last Modified Time'),
];

export const PURCHASE_RECEIVE_LINE_FIELDS: IDataSourceFieldDescriptor[] = [
  ref('receive_id', 'Receive ID', PURCHASE_RECEIVES_SOURCE_ID, 'receive_id', 'receive_number'),
  text('receive_number', 'Receive Number'),
  date('date', 'Receive Date'),
  text('status', 'Status'),
  purchaseOrderRef(),
  text('purchaseorder_number', 'Purchase Order Number'),
  vendorRef(),
  text('vendor_name', 'Vendor Name'),
  text('line_item_id', 'Line Item ID'),
  num('item_order', 'Line Position'),
  itemRef(),
  text('item_name', 'Item Name'),
  text('sku', 'SKU'),
  text('description', 'Description'),
  num('quantity_received', 'Quantity Received'),
  num('quantity_billed', 'Quantity Billed'),
  text('unit', 'Unit'),
  locationRef(),
  text('location_name', 'Location Name'),
  bool('is_billed', 'Billed'),
  date('created_time', 'Receive Created Time'),
  date('last_modified_time', 'Receive Last Modified Time'),
];

export const PACKAGE_FIELDS: IDataSourceFieldDescriptor[] = [
  text('package_id', 'Package ID'),
  text('package_number', 'Package Number'),
  salesOrderRef(),
  text('salesorder_number', 'Sales Order Number'),
  customerRef(),
  text('customer_name', 'Customer Name'),
  date('date', 'Package Date'),
  text('status', 'Status'),
  text('shipment_id', 'Shipment ID'),
  text('shipment_number', 'Shipment Number'),
  date('shipment_date', 'Shipment Date'),
  text('delivery_method', 'Delivery Method'),
  text('tracking_number', 'Tracking Number'),
  num('total_quantity', 'Total Quantity'),
  date('created_time', 'Created Time'),
  date('last_modified_time', 'Last Modified Time'),
];

/** Shared by package lines and shipment lines, which are the same row on the API side. */
export const PACKAGE_LINE_FIELDS: IDataSourceFieldDescriptor[] = [
  ref('package_id', 'Package ID', PACKAGES_SOURCE_ID, 'package_id', 'package_number'),
  text('package_number', 'Package Number'),
  date('package_date', 'Package Date'),
  text('package_status', 'Package Status'),
  salesOrderRef(),
  text('salesorder_number', 'Sales Order Number'),
  customerRef(),
  text('customer_name', 'Customer Name'),
  text('line_item_id', 'Line Item ID'),
  text('salesorder_line_item_id', 'Sales Order Line Item ID'),
  num('item_order', 'Line Position'),
  itemRef(),
  text('item_name', 'Item Name'),
  text('sku', 'SKU'),
  text('description', 'Description'),
  num('quantity', 'Quantity'),
  text('unit', 'Unit'),
  bool('is_invoiced', 'Invoiced'),
  text('shipment_id', 'Shipment ID'),
  text('shipment_number', 'Shipment Number'),
  date('shipment_date', 'Shipment Date'),
  text('shipment_status', 'Shipment Status'),
  text('carrier', 'Carrier'),
  text('tracking_number', 'Tracking Number'),
  text('delivery_method', 'Delivery Method'),
  date('created_time', 'Package Created Time'),
  date('last_modified_time', 'Package Last Modified Time'),
];

export const SHIPMENT_FIELDS: IDataSourceFieldDescriptor[] = [
  text('shipment_id', 'Shipment ID'),
  text('shipment_number', 'Shipment Number'),
  salesOrderRef(),
  text('salesorder_number', 'Sales Order Number'),
  customerRef(),
  text('customer_name', 'Customer Name'),
  date('shipment_date', 'Shipment Date'),
  text('status', 'Status'),
  text('detailed_status', 'Detailed Status'),
  text('carrier', 'Carrier'),
  text('service', 'Service'),
  text('delivery_method', 'Delivery Method'),
  text('tracking_number', 'Tracking Number'),
  num('shipment_rate', 'Shipment Rate'),
  num('delivery_days', 'Delivery Days'),
  bool('delivery_guarantee', 'Delivery Guarantee'),
  text('notes', 'Notes'),
  text('shipping_address', 'Shipping Address'),
  text('shipping_city', 'Shipping City'),
  text('shipping_state', 'Shipping State'),
  text('shipping_postcode', 'Shipping Postcode'),
  text('shipping_country', 'Shipping Country'),
  text('package_ids', 'Package IDs'),
  text('package_numbers', 'Package Numbers'),
  num('package_count', 'Package Count'),
];

export const CONTACT_PERSON_FIELDS: IDataSourceFieldDescriptor[] = [
  text('contact_person_id', 'Contact Person ID'),
  customerRef(),
  text('customer_name', 'Customer Name'),
  text('company_name', 'Company Name'),
  text('customer_status', 'Customer Status'),
  text('salutation', 'Salutation'),
  text('first_name', 'First Name'),
  text('last_name', 'Last Name'),
  text('full_name', 'Full Name'),
  text('email', 'Email'),
  text('phone', 'Phone'),
  text('mobile', 'Mobile'),
  text('designation', 'Designation'),
  text('department', 'Department'),
  bool('is_primary_contact', 'Primary Contact'),
  bool('is_added_in_portal', 'In Customer Portal'),
];

export const SALESPERSON_FIELDS: IDataSourceFieldDescriptor[] = [
  text('salesperson_id', 'Salesperson ID'),
  text('salesperson_name', 'Salesperson'),
  text('email', 'Email'),
  text('mobile', 'Mobile'),
  bool('is_active', 'Active'),
];

const STOCK_ITEM_FIELDS: IDataSourceFieldDescriptor[] = [
  itemRef(),
  text('item_name', 'Item Name'),
  text('sku', 'SKU'),
  text('unit', 'Unit'),
  text('status', 'Item Status'),
  text('item_type', 'Item Type'),
  text('product_type', 'Product Type'),
  text('category_name', 'Category'),
  text('brand', 'Brand'),
  date('last_modified_time', 'Item Last Modified Time'),
];

export const INVENTORY_BY_ITEM_FIELDS: IDataSourceFieldDescriptor[] = [
  ...STOCK_ITEM_FIELDS,
  num('reorder_level', 'Reorder Level'),
  vendorRef('preferred_vendor_id', 'Preferred Vendor ID'),
  text('preferred_vendor_name', 'Preferred Vendor'),
  num('stock_on_hand', 'Stock on Hand'),
  num('available_stock', 'Available Stock'),
  num('actual_available_stock', 'Actual Available Stock'),
  num('committed_stock', 'Committed Stock'),
  num('actual_committed_stock', 'Actual Committed Stock'),
  num('available_for_sale_stock', 'Available for Sale'),
  num('actual_available_for_sale_stock', 'Actual Available for Sale'),
];

export const INVENTORY_BY_LOCATION_FIELDS: IDataSourceFieldDescriptor[] = [
  ...STOCK_ITEM_FIELDS,
  locationRef(),
  text('location_name', 'Location Name'),
  text('location_status', 'Location Status'),
  bool('is_primary_location', 'Primary Location'),
  num('stock_on_hand', 'Stock on Hand'),
  num('available_stock', 'Available Stock'),
  num('actual_available_stock', 'Actual Available Stock'),
];

export const INVOICE_FIELDS: IDataSourceFieldDescriptor[] = [
  text('invoice_id', 'Invoice ID'),
  text('invoice_number', 'Invoice Number'),
  customerRef(),
  text('customer_name', 'Customer Name'),
  date('date', 'Invoice Date'),
  date('due_date', 'Due Date'),
  text('status', 'Status'),
  text('reference_number', 'Reference Number'),
  salespersonRef(),
  text('salesperson_name', 'Salesperson'),
  text('currency_id', 'Currency ID'),
  text('currency_code', 'Currency'),
  num('exchange_rate', 'Exchange Rate'),
  num('total', 'Total'),
  num('balance', 'Balance'),
  num('write_off_amount', 'Write-off Amount'),
  date('payment_expected_date', 'Payment Expected Date'),
  date('last_payment_date', 'Last Payment Date'),
  locationRef(),
  text('location_name', 'Location Name'),
  bool('is_emailed', 'Emailed'),
  bool('is_viewed_by_client', 'Viewed by Customer'),
  date('created_time', 'Created Time'),
  date('last_modified_time', 'Last Modified Time'),
];

export const INVOICE_LINE_FIELDS: IDataSourceFieldDescriptor[] = [
  ref('invoice_id', 'Invoice ID', INVOICES_SOURCE_ID, 'invoice_id', 'invoice_number'),
  text('invoice_number', 'Invoice Number'),
  customerRef(),
  text('customer_name', 'Customer Name'),
  date('invoice_date', 'Invoice Date'),
  date('due_date', 'Due Date'),
  text('invoice_status', 'Invoice Status'),
  text('reference_number', 'Reference Number'),
  salespersonRef(),
  text('salesperson_name', 'Salesperson'),
  text('currency_code', 'Currency'),
  num('exchange_rate', 'Exchange Rate'),
  salesOrderRef(),
  text('salesorder_number', 'Sales Order Number'),
  text('salesorder_item_id', 'Sales Order Line Item ID'),
  text('line_item_id', 'Line Item ID'),
  num('item_order', 'Line Position'),
  itemRef(),
  text('item_name', 'Item Name'),
  text('sku', 'SKU'),
  text('description', 'Description'),
  num('quantity', 'Quantity Invoiced'),
  text('unit', 'Unit'),
  num('rate', 'Rate'),
  num('bcy_rate', 'Rate (Base Currency)'),
  num('discount', 'Discount'),
  text('tax_name', 'Tax'),
  num('tax_percentage', 'Tax %'),
  num('item_total', 'Line Total'),
  locationRef(),
  text('location_name', 'Location Name'),
  date('created_time', 'Invoice Created Time'),
  date('last_modified_time', 'Invoice Last Modified Time'),
];

export const BILL_FIELDS: IDataSourceFieldDescriptor[] = [
  text('bill_id', 'Bill ID'),
  text('bill_number', 'Bill Number'),
  vendorRef(),
  text('vendor_name', 'Vendor Name'),
  date('date', 'Bill Date'),
  date('due_date', 'Due Date'),
  text('status', 'Status'),
  text('current_sub_status', 'Sub-status'),
  text('reference_number', 'Reference Number'),
  text('currency_id', 'Currency ID'),
  text('currency_code', 'Currency'),
  num('exchange_rate', 'Exchange Rate'),
  num('total', 'Total'),
  num('balance', 'Balance'),
  date('created_time', 'Created Time'),
  date('last_modified_time', 'Last Modified Time'),
];

export const BILL_LINE_FIELDS: IDataSourceFieldDescriptor[] = [
  ref('bill_id', 'Bill ID', BILLS_SOURCE_ID, 'bill_id', 'bill_number'),
  text('bill_number', 'Bill Number'),
  date('bill_date', 'Bill Date'),
  date('due_date', 'Due Date'),
  text('bill_status', 'Bill Status'),
  vendorRef(),
  text('vendor_name', 'Vendor Name'),
  text('reference_number', 'Reference Number'),
  text('currency_code', 'Currency'),
  num('exchange_rate', 'Exchange Rate'),
  purchaseOrderRef(),
  text('purchaseorder_number', 'Purchase Order Number'),
  text('purchaseorder_item_id', 'Purchase Order Line Item ID'),
  ref('receive_id', 'Receive ID', PURCHASE_RECEIVES_SOURCE_ID, 'receive_id', 'receive_number'),
  text('receive_number', 'Receive Number'),
  text('receive_line_item_id', 'Receive Line Item ID'),
  text('line_item_id', 'Line Item ID'),
  num('item_order', 'Line Position'),
  itemRef(),
  text('item_name', 'Item Name'),
  text('sku', 'SKU'),
  text('description', 'Description'),
  text('account_name', 'Account'),
  num('quantity', 'Quantity Billed'),
  text('unit', 'Unit'),
  num('rate', 'Rate'),
  num('bcy_rate', 'Rate (Base Currency)'),
  text('tax_name', 'Tax'),
  num('tax_percentage', 'Tax %'),
  num('item_total', 'Line Total'),
  locationRef(),
  text('location_name', 'Location Name'),
  date('created_time', 'Bill Created Time'),
  date('last_modified_time', 'Bill Last Modified Time'),
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
 * Paging happens inside Intera.ZohoInventory.Api rather than upstream: Zoho pages documents while
 * the line sources answer in lines, so a page number cannot be handed to it. The API therefore
 * filters what it read and pages the result, which is also what makes `X-Total-Count` exact.
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


/**
 * One data source of this integration. `schemaKey` is the source's name in the API's
 * `GET /api/Schema` answer, which is how `provisionInstance` finds its custom fields.
 */
export interface IZohoInventorySourceDefinition {
  id: string;
  name: string;
  description: string;
  fields: IDataSourceFieldDescriptor[];
  apiPath: string;
  schemaKey: string;
}

export const ZOHO_INVENTORY_SOURCES: IZohoInventorySourceDefinition[] = [
  {
    id: CUSTOMERS_SOURCE_ID,
    name: 'Customers',
    description:
      'Customer master: Zoho contacts of type customer, active and inactive, with receivable balances. ' +
      'Addresses are not here, because Zoho only returns them one contact at a time; contact persons ' +
      'are the Contacts source.',
    fields: CUSTOMER_FIELDS,
    apiPath: CUSTOMERS_QUERY_API_PATH,
    schemaKey: 'customers',
  },
  {
    id: VENDORS_SOURCE_ID,
    name: 'Vendors',
    description: 'Supplier master: Zoho contacts of type vendor, active and inactive, with payable balances.',
    fields: VENDOR_FIELDS,
    apiPath: VENDORS_QUERY_API_PATH,
    schemaKey: 'vendors',
  },
  {
    id: ITEMS_SOURCE_ID,
    name: 'Items',
    description:
      'Product and service catalogue with organization-wide stock (`stock_on_hand`, `available_stock`). ' +
      'Committed stock is in Inventory by Item, stock per location in Inventory by Location.',
    fields: ITEM_FIELDS,
    apiPath: ITEMS_QUERY_API_PATH,
    schemaKey: 'items',
  },
  {
    id: LOCATIONS_SOURCE_ID,
    name: 'Locations',
    description: 'Warehouses, branches and stock locations.',
    fields: LOCATION_FIELDS,
    apiPath: LOCATIONS_QUERY_API_PATH,
    schemaKey: 'locations',
  },
  {
    id: SALES_ORDERS_SOURCE_ID,
    name: 'Sales Orders',
    description:
      'One row per customer order, with the ordered, invoiced, packed and shipped quantities Zoho keeps ' +
      'on the order. Cheap: read from the order list alone.',
    fields: SALES_ORDER_FIELDS,
    apiPath: SALES_ORDERS_QUERY_API_PATH,
    schemaKey: 'sales_orders',
  },
  {
    id: SALES_ORDER_LINES_SOURCE_ID,
    name: 'Sales Order Lines',
    description:
      'One row per ordered item: `quantity` against `quantity_packed`, `quantity_shipped` and ' +
      '`quantity_invoiced`. **Each order is one Zoho call**, so filter on the order fields ' +
      '(`customer_name`, `order_date`, `order_status`, `salesorder_number`) and only matching orders are ' +
      'read. Order totals are not repeated on the line; count orders with `countDistinct(salesorder_id)`.',
    fields: SALES_ORDER_LINE_FIELDS,
    apiPath: SALES_ORDER_LINES_QUERY_API_PATH,
    schemaKey: 'sales_order_lines',
  },
  {
    id: PURCHASE_ORDERS_SOURCE_ID,
    name: 'Purchase Orders',
    description: 'One row per order placed on a supplier. Read from the order list alone.',
    fields: PURCHASE_ORDER_FIELDS,
    apiPath: PURCHASE_ORDERS_QUERY_API_PATH,
    schemaKey: 'purchase_orders',
  },
  {
    id: PURCHASE_ORDER_LINES_SOURCE_ID,
    name: 'Purchase Order Lines',
    description:
      'One row per item ordered from a supplier, with `quantity_received`. `salesorder_item_id` links a ' +
      'line to the customer order line it was bought for. **Each order is one Zoho call**, so filter on ' +
      '`vendor_name`, `order_date` or `po_status` first.',
    fields: PURCHASE_ORDER_LINE_FIELDS,
    apiPath: PURCHASE_ORDER_LINES_QUERY_API_PATH,
    schemaKey: 'purchase_order_lines',
  },
  {
    id: PURCHASE_RECEIVES_SOURCE_ID,
    name: 'Purchase Receives',
    description: 'One row per receipt of goods from a supplier.',
    fields: PURCHASE_RECEIVE_FIELDS,
    apiPath: PURCHASE_RECEIVES_QUERY_API_PATH,
    schemaKey: 'purchase_receives',
  },
  {
    id: PURCHASE_RECEIVE_LINES_SOURCE_ID,
    name: 'Purchase Receive Lines',
    description:
      'What was physically received, one row per item. **Each receipt is one Zoho call**; a `date` range ' +
      'and a `vendor_id` are applied by Zoho before anything is read.',
    fields: PURCHASE_RECEIVE_LINE_FIELDS,
    apiPath: PURCHASE_RECEIVE_LINES_QUERY_API_PATH,
    schemaKey: 'purchase_receive_lines',
  },
  {
    id: PACKAGES_SOURCE_ID,
    name: 'Packages',
    description: 'One row per package packed from a sales order.',
    fields: PACKAGE_FIELDS,
    apiPath: PACKAGES_QUERY_API_PATH,
    schemaKey: 'packages',
  },
  {
    id: PACKAGE_LINES_SOURCE_ID,
    name: 'Package Lines',
    description:
      'What has been packed, one row per item, with the shipment of the package when there is one. ' +
      '`salesorder_line_item_id` links back to the order line. **Each package is one Zoho call.**',
    fields: PACKAGE_LINE_FIELDS,
    apiPath: PACKAGE_LINES_QUERY_API_PATH,
    schemaKey: 'package_lines',
  },
  {
    id: SHIPMENTS_SOURCE_ID,
    name: 'Shipments',
    description:
      'One row per shipment: carrier, tracking, status, delivery. Zoho has no shipment list, so shipments ' +
      'are read out of their packages, **one Zoho call per shipped package**.',
    fields: SHIPMENT_FIELDS,
    apiPath: SHIPMENTS_QUERY_API_PATH,
    schemaKey: 'shipments',
  },
  {
    id: SHIPMENT_LINES_SOURCE_ID,
    name: 'Shipment Lines',
    description:
      'What actually travelled: lines of shipped packages, with the shipment on each. A `shipment_date` ' +
      'range is applied by Zoho before anything is read. **One Zoho call per shipped package.**',
    fields: PACKAGE_LINE_FIELDS,
    apiPath: SHIPMENT_LINES_QUERY_API_PATH,
    schemaKey: 'shipment_lines',
  },
  {
    id: CONTACTS_SOURCE_ID,
    name: 'Contacts',
    description:
      'Contact persons of customers, one row per person. **Each customer is one Zoho call**, so filter on ' +
      '`customer_id` or `customer_name` and only matching customers are read.',
    fields: CONTACT_PERSON_FIELDS,
    apiPath: CONTACTS_QUERY_API_PATH,
    schemaKey: 'contacts',
  },
  {
    id: SALESPERSONS_SOURCE_ID,
    name: 'Salespersons',
    description: 'Salespersons (account managers), active and inactive. One Zoho call.',
    fields: SALESPERSON_FIELDS,
    apiPath: SALESPERSONS_QUERY_API_PATH,
    schemaKey: 'salespersons',
  },
  {
    id: INVENTORY_BY_ITEM_SOURCE_ID,
    name: 'Inventory by Item',
    description:
      'Current stock per item, organization-wide, with committed stock and the preferred vendor. ' +
      '`stock_on_hand`/`available_stock` are the accounting stock, the `actual_…` columns the physical ' +
      'stock. One Zoho call per 100 items after the item list; services that track no inventory are not here.',
    fields: INVENTORY_BY_ITEM_FIELDS,
    apiPath: INVENTORY_BY_ITEM_QUERY_API_PATH,
    schemaKey: 'inventory_by_item',
  },
  {
    id: INVENTORY_BY_LOCATION_SOURCE_ID,
    name: 'Inventory by Location',
    description:
      'Current stock per item and location. Zoho splits stock by location only with multi-warehouse ' +
      'enabled; without it each item is one row at the organization\'s only location. One Zoho call per ' +
      '100 items after the item list.',
    fields: INVENTORY_BY_LOCATION_FIELDS,
    apiPath: INVENTORY_BY_LOCATION_QUERY_API_PATH,
    schemaKey: 'inventory_by_location',
  },
  {
    id: INVOICES_SOURCE_ID,
    name: 'Invoices',
    description: 'One row per customer invoice, with its balance. Read from the invoice list alone.',
    fields: INVOICE_FIELDS,
    apiPath: INVOICES_QUERY_API_PATH,
    schemaKey: 'invoices',
  },
  {
    id: INVOICE_LINES_SOURCE_ID,
    name: 'Invoice Lines',
    description:
      'What was invoiced, one row per item, with the sales order line it bills. **Each invoice is one ' +
      'Zoho call**, so filter on `customer_name`, `invoice_date` or `invoice_status` first.',
    fields: INVOICE_LINE_FIELDS,
    apiPath: INVOICE_LINES_QUERY_API_PATH,
    schemaKey: 'invoice_lines',
  },
  {
    id: BILLS_SOURCE_ID,
    name: 'Bills',
    description: 'One row per vendor bill, with its balance. Read from the bill list alone.',
    fields: BILL_FIELDS,
    apiPath: BILLS_QUERY_API_PATH,
    schemaKey: 'bills',
  },
  {
    id: BILL_LINES_SOURCE_ID,
    name: 'Bill Lines',
    description:
      'What the supplier billed, one row per item, with the purchase order line and the receive line it ' +
      'bills: ordered → received → billed. **Each bill is one Zoho call**, so filter on `vendor_name`, ' +
      '`bill_date` or `bill_status` first.',
    fields: BILL_LINE_FIELDS,
    apiPath: BILL_LINES_QUERY_API_PATH,
    schemaKey: 'bill_lines',
  },
];

function createZohoInventoryDataSource(source: IZohoInventorySourceDefinition): IDataSource {
  return {
    id: source.id,
    name: source.name,
    description: source.description,
    fields: source.fields,
    async getData(params: IDataSourceParams, context: IDataSourceContext): Promise<unknown> {
      const query = normalizeDataQuery(params);
      const client = createZohoInventoryClient(readZohoInventoryConfig(context));
      return client.queryRaw(source.apiPath, query, context?.signal);
    },
  };
}

export const zohoInventoryDataSources: IDataSource[] = ZOHO_INVENTORY_SOURCES.map(createZohoInventoryDataSource);
