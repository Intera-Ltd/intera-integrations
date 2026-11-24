using Intera.Common.Infrastructure.Query;

namespace Intera.Unleashed.Api.Models.Query;

/// <summary>
/// The field allowlists for the nine Unleashed endpoints. Every name here must be a key
/// <see cref="UnleashedRows"/> actually writes, and must also exist in the matching descriptor list
/// in <c>intera-unleashed-integration</c>: the engine rejects a field it does not know, so a rename
/// has to happen in all three places at once.
/// </summary>
/// <remarks>
/// The lists carry no type information, unlike Salesforce's <c>SoqlField</c>. They do not need any:
/// nothing here is compiled into an upstream query language, so a value is only ever compared against
/// what the row already holds, and <c>InMemoryQueryEngine</c> reads the type off the value itself.
/// </remarks>
public static class WarehouseQueryFields
{
    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> All = InMemoryQueryFieldsBuilder.Build(
        "warehouse_id",
        "warehouse_code",
        "warehouse_name",
        "is_default",
        "is_obsolete",
        "street_no",
        "address_line1",
        "address_line2",
        "city",
        "region",
        "country",
        "post_code",
        "phone_number",
        "fax_number",
        "mobile_number",
        "ddi_number",
        "contact_name",
        "last_modified_on");
}

public static class ProductQueryFields
{
    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> All = InMemoryQueryFieldsBuilder.Build(
        "product_id",
        "product_code",
        "product_description",
        "barcode",
        "product_group",
        "product_sub_group",
        "product_brand",
        "unit_of_measure",
        "purchase_unit_of_measure",
        "supplier_code",
        "supplier_name",
        "pack_size",
        "width",
        "height",
        "depth",
        "weight",
        "min_stock_alert_level",
        "max_stock_alert_level",
        "last_cost",
        "default_purchase_price",
        "default_sell_price",
        "average_land_price",
        "nominal_cost",
        "minimum_sell_price",
        "minimum_sale_quantity",
        "minimum_order_quantity",
        "is_obsolete",
        "never_diminishing",
        "is_component",
        "is_assembled_product",
        "is_serialized",
        "is_batch_tracked",
        "is_purchasable",
        "is_sellable",
        "bin_location",
        "notes",
        "created_on",
        "created_by",
        "last_modified_on",
        "last_modified_by");
}

public static class StockOnHandQueryFields
{
    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> All = InMemoryQueryFieldsBuilder.Build(
        "stock_on_hand_id",
        "product_id",
        "product_code",
        "product_description",
        "product_group",
        "warehouse_id",
        "warehouse_code",
        "warehouse_name",
        "qty_on_hand",
        "allocated_qty",
        "available_qty",
        "on_purchase",
        "avg_cost",
        "total_cost",
        "days_since_last_sale",
        "last_modified_on");
}

public static class PurchaseOrderLineQueryFields
{
    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> All = InMemoryQueryFieldsBuilder.Build(
        "order_id",
        "order_number",
        "order_date",
        "completed_date",
        "received_date",
        "supplier_invoice_date",
        "order_status",
        "custom_order_status",
        "supplier_code",
        "supplier_name",
        "supplier_ref",
        "warehouse_code",
        "warehouse_name",
        "currency_code",
        "exchange_rate",
        "order_comments",
        "created_on",
        "created_by",
        "last_modified_on",
        "last_modified_by",
        "line_id",
        "line_number",
        "product_id",
        "product_code",
        "product_description",
        "product_group",
        "unit_of_measure",
        "delivery_date",
        "order_quantity",
        "receipt_quantity",
        "outstanding_quantity",
        "purchase_unit_order_quantity",
        "purchase_unit_receipt_quantity",
        "unit_price",
        "discounted_unit_price",
        "discount_rate",
        "line_total",
        "line_tax",
        "line_tax_code",
        "line_tax_rate",
        "volume",
        "weight",
        "line_comments");
}

public static class SalesOrderLineQueryFields
{
    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> All = InMemoryQueryFieldsBuilder.Build(
        "order_id",
        "order_number",
        "order_date",
        "required_date",
        "completed_date",
        "received_date",
        "payment_due_date",
        "order_status",
        "custom_order_status",
        "customer_code",
        "customer_name",
        "customer_ref",
        "warehouse_code",
        "warehouse_name",
        "currency_code",
        "exchange_rate",
        "salesperson_name",
        "sales_order_group",
        "delivery_method",
        "delivery_city",
        "delivery_region",
        "delivery_country",
        "delivery_post_code",
        "order_comments",
        "created_on",
        "created_by",
        "last_modified_on",
        "last_modified_by",
        "line_id",
        "line_number",
        "line_type",
        "product_id",
        "product_code",
        "product_description",
        "order_quantity",
        "unit_price",
        "discount_rate",
        "discounted_unit_price",
        "line_total",
        "line_tax",
        "line_tax_code",
        "unit_cost",
        "volume",
        "weight",
        "line_comments");
}

public static class SalesShipmentLineQueryFields
{
    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> All = InMemoryQueryFieldsBuilder.Build(
        "shipment_id",
        "shipment_number",
        "shipment_status",
        "dispatch_date",
        "order_id",
        "order_number",
        "tracking_number",
        "shipping_company",
        "customer_code",
        "customer_name",
        "customer_ref",
        "warehouse_code",
        "warehouse_name",
        "delivery_city",
        "delivery_region",
        "delivery_country",
        "delivery_post_code",
        "number_of_packages",
        "shipment_weight",
        "incoterm",
        "shipment_comments",
        "created_on",
        "created_by",
        "last_modified_on",
        "last_modified_by",
        "line_id",
        "line_number",
        "product_id",
        "product_code",
        "product_description",
        "shipment_qty",
        "unit_cost",
        "unit_cost_at_time_of_dispatch",
        "commercial_monetary_value",
        "sales_order_line_number");
}

public static class StockTransferLineQueryFields
{
    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> All = InMemoryQueryFieldsBuilder.Build(
        "transfer_id",
        "transfer_order_number",
        "order_date",
        "delivery_date",
        "transfer_status",
        "source_warehouse_code",
        "source_warehouse_name",
        "destination_warehouse_code",
        "destination_warehouse_name",
        "transfer_comments",
        "created_on",
        "created_by",
        "last_modified_on",
        "last_modified_by",
        "line_id",
        "line_number",
        "product_id",
        "product_code",
        "transfer_quantity",
        "available_quantity",
        "source_stock_on_hand_before_transfer",
        "average_landed_price_at_time_of_transfer",
        "receipt_fifo_date",
        "line_comments");
}

public static class StockCountLineQueryFields
{
    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> All = InMemoryQueryFieldsBuilder.Build(
        "stock_count_id",
        "stock_take_code",
        "stock_take_name",
        "description",
        "count_type",
        "status",
        "count_date",
        "completed_date",
        "warehouse_code",
        "warehouse_name",
        "last_modified_on",
        "line_id",
        "line_number",
        "product_id",
        "product_code",
        "product_description",
        "on_hand_quantity",
        "available_quantity",
        "count_quantity",
        "average_land_price_at_time_of_stock_take",
        "expiry_date",
        "receipt_fifo_date");
}

public static class SupplierReturnLineQueryFields
{
    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> All = InMemoryQueryFieldsBuilder.Build(
        "supplier_return_id",
        "supplier_return_number",
        "status",
        "return_date",
        "supplier_code",
        "supplier_name",
        "supplier_ref",
        "purchase_order_number",
        "warehouse_code",
        "warehouse_name",
        "currency_code",
        "exchange_rate",
        "return_comments",
        "created_on",
        "created_by",
        "last_modified_on",
        "last_modified_by",
        "line_id",
        "line_number",
        "product_id",
        "product_code",
        "product_description",
        "order_quantity",
        "order_price",
        "return_quantity",
        "return_price",
        "return_cost",
        "purchase_unit_order_quantity",
        "purchase_unit_return_quantity",
        "return_reason",
        "return_to_supplier",
        "tax_rate",
        "line_tax",
        "average_landed_price_before_return",
        "average_landed_price_after_return",
        "stock_on_hand_before_return",
        "required_date",
        "line_comments");
}
