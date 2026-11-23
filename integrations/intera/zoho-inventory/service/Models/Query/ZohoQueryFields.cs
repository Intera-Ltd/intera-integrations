using Intera.Common.Infrastructure.Query;
using Intera.ZohoInventory.Api.Infrastructure;

namespace Intera.ZohoInventory.Api.Models.Query;

/// <summary>
/// The field allowlists for the Zoho Inventory endpoints. Every name here must be a key
/// <see cref="ZohoRows"/> actually writes, and must also exist in the matching descriptor list in
/// <c>intera-zoho-inventory-integration</c>: the engine rejects a field it does not know, so a rename
/// has to happen in all three places at once.
/// </summary>
/// <remarks>
/// Custom fields are not listed: each allowlist admits any <c>cf_…</c> name, and the line sources
/// also <c>item_cf_…</c> (see <see cref="ZohoCustomFields"/>).
/// </remarks>
public static class ZohoQueryFields
{
    private static readonly string[] ContactColumns =
    [
        "company_name",
        "website",
        "contact_type",
        "status",
        "currency_id",
        "currency_code",
        "payment_terms",
        "payment_terms_label",
        "first_name",
        "last_name",
        "email",
        "phone",
        "mobile",
        "created_time",
        "last_modified_time"
    ];

    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> Customers = Header(
    [
        "customer_id",
        "customer_name",
        .. ContactColumns,
        "outstanding_receivable_amount",
        "unused_credits_receivable_amount"
    ]);

    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> Vendors = Header(
    [
        "vendor_id",
        "vendor_name",
        .. ContactColumns,
        "outstanding_payable_amount",
        "unused_credits_payable_amount"
    ]);

    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> Items = Header(
    [
        "item_id",
        "item_name",
        "sku",
        "description",
        "status",
        "item_type",
        "product_type",
        "unit",
        "group_id",
        "group_name",
        "category_id",
        "category_name",
        "brand",
        "manufacturer",
        "sales_rate",
        "purchase_rate",
        "tax_name",
        "tax_percentage",
        "is_combo_product",
        "can_be_sold",
        "can_be_purchased",
        "track_inventory",
        "is_returnable",
        "preferred_vendor_name",
        "reorder_level",
        "stock_on_hand",
        "available_stock",
        "available_for_sale",
        "actual_available_stock",
        "upc",
        "ean",
        "isbn",
        "part_number",
        "created_time",
        "last_modified_time"
    ]);

    /// <summary>Locations carry no custom fields, so this is the one plain allowlist.</summary>
    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> Locations = InMemoryQueryFieldsBuilder.Build(
        "location_id",
        "location_name",
        "type",
        "parent_location_id",
        "is_primary",
        "is_active",
        "email",
        "phone",
        "address",
        "address2",
        "city",
        "state",
        "state_code",
        "postal_code",
        "country");

    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> SalesOrders = Header(
    [
        "salesorder_id",
        "salesorder_number",
        "customer_id",
        "customer_name",
        "date",
        "shipment_date",
        "delivery_date",
        "status",
        "order_status",
        "current_sub_status",
        "invoiced_status",
        "paid_status",
        "shipped_status",
        "reference_number",
        "salesperson_name",
        "delivery_method",
        "sales_channel",
        "currency_id",
        "currency_code",
        "quantity",
        "quantity_invoiced",
        "quantity_packed",
        "quantity_shipped",
        "total",
        "bcy_total",
        "total_invoiced_amount",
        "balance",
        "is_drop_shipment",
        "is_backorder",
        "is_emailed",
        "created_time",
        "last_modified_time"
    ]);

    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> SalesOrderLines = Lines(
    [
        "salesorder_id",
        "salesorder_number",
        "customer_id",
        "customer_name",
        "order_date",
        "shipment_date",
        "order_status",
        "reference_number",
        "salesperson_id",
        "salesperson_name",
        "currency_code",
        "exchange_rate",
        "line_item_id",
        "item_order",
        "item_id",
        "item_name",
        "sku",
        "description",
        "quantity",
        "quantity_invoiced",
        "quantity_packed",
        "quantity_shipped",
        "quantity_delivered",
        "quantity_cancelled",
        "quantity_returned",
        "unit",
        "rate",
        "bcy_rate",
        "tax_name",
        "tax_percentage",
        "item_total",
        "is_invoiced",
        "is_combo_product",
        "combo_type",
        "location_id",
        "location_name",
        "created_time",
        "last_modified_time"
    ]);

    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> PurchaseOrders = Header(
    [
        "purchaseorder_id",
        "purchaseorder_number",
        "vendor_id",
        "vendor_name",
        "date",
        "delivery_date",
        "expected_delivery_date",
        "status",
        "order_status",
        "received_status",
        "billed_status",
        "reference_number",
        "currency_id",
        "currency_code",
        "total",
        "total_ordered_quantity",
        "quantity_yet_to_receive",
        "quantity_marked_as_received",
        "is_po_marked_as_received",
        "is_drop_shipment",
        "is_backorder",
        "created_time",
        "last_modified_time"
    ]);

    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> PurchaseOrderLines = Lines(
    [
        "purchaseorder_id",
        "purchaseorder_number",
        "vendor_id",
        "vendor_name",
        "order_date",
        "delivery_date",
        "expected_delivery_date",
        "po_status",
        "reference_number",
        "salesorder_id",
        "currency_code",
        "exchange_rate",
        "line_item_id",
        "item_order",
        "item_id",
        "item_name",
        "sku",
        "description",
        "salesorder_item_id",
        "quantity",
        "quantity_received",
        "quantity_billed",
        "quantity_cancelled",
        "unit",
        "purchase_rate",
        "bcy_rate",
        "tax_name",
        "tax_percentage",
        "item_total",
        "location_id",
        "location_name",
        "created_time",
        "last_modified_time"
    ]);

    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> PurchaseReceives = Header(
    [
        "receive_id",
        "receive_number",
        "date",
        "status",
        "received_status",
        "billed_status",
        "purchaseorder_id",
        "purchaseorder_number",
        "vendor_id",
        "vendor_name",
        "quantity_received",
        "location_id",
        "location_name",
        "is_salesreturn",
        "created_time",
        "last_modified_time"
    ]);

    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> PurchaseReceiveLines = Lines(
    [
        "receive_id",
        "receive_number",
        "date",
        "status",
        "purchaseorder_id",
        "purchaseorder_number",
        "vendor_id",
        "vendor_name",
        "line_item_id",
        "item_order",
        "item_id",
        "item_name",
        "sku",
        "description",
        "quantity_received",
        "quantity_billed",
        "unit",
        "location_id",
        "location_name",
        "is_billed",
        "created_time",
        "last_modified_time"
    ]);

    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> Packages = Header(
    [
        "package_id",
        "package_number",
        "salesorder_id",
        "salesorder_number",
        "customer_id",
        "customer_name",
        "date",
        "status",
        "shipment_id",
        "shipment_number",
        "shipment_date",
        "delivery_method",
        "tracking_number",
        "total_quantity",
        "created_time",
        "last_modified_time"
    ]);

    /// <summary>Shared by <c>package_lines</c> and <c>shipment_lines</c>, which are the same row.</summary>
    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> PackageLines = Lines(
    [
        "package_id",
        "package_number",
        "package_date",
        "package_status",
        "salesorder_id",
        "salesorder_number",
        "customer_id",
        "customer_name",
        "line_item_id",
        "salesorder_line_item_id",
        "item_order",
        "item_id",
        "item_name",
        "sku",
        "description",
        "quantity",
        "unit",
        "is_invoiced",
        "shipment_id",
        "shipment_number",
        "shipment_date",
        "shipment_status",
        "carrier",
        "tracking_number",
        "delivery_method",
        "created_time",
        "last_modified_time"
    ]);

    /// <summary>
    /// Shipments carry no custom fields here: they live on the shipment order itself, which is only
    /// reachable by id and would cost one more call per shipment.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> Shipments = InMemoryQueryFieldsBuilder.Build(
        "shipment_id",
        "shipment_number",
        "salesorder_id",
        "salesorder_number",
        "customer_id",
        "customer_name",
        "shipment_date",
        "status",
        "detailed_status",
        "carrier",
        "service",
        "delivery_method",
        "tracking_number",
        "shipment_rate",
        "delivery_days",
        "delivery_guarantee",
        "notes",
        "shipping_address",
        "shipping_city",
        "shipping_state",
        "shipping_postcode",
        "shipping_country",
        "package_ids",
        "package_numbers",
        "package_count");

    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> Invoices = Header(
    [
        "invoice_id",
        "invoice_number",
        "customer_id",
        "customer_name",
        "date",
        "due_date",
        "status",
        "reference_number",
        "salesperson_id",
        "salesperson_name",
        "currency_id",
        "currency_code",
        "exchange_rate",
        "total",
        "balance",
        "write_off_amount",
        "payment_expected_date",
        "last_payment_date",
        "location_id",
        "location_name",
        "is_emailed",
        "is_viewed_by_client",
        "created_time",
        "last_modified_time"
    ]);

    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> InvoiceLines = Lines(
    [
        "invoice_id",
        "invoice_number",
        "customer_id",
        "customer_name",
        "invoice_date",
        "due_date",
        "invoice_status",
        "reference_number",
        "salesperson_id",
        "salesperson_name",
        "currency_code",
        "exchange_rate",
        "salesorder_id",
        "salesorder_number",
        "salesorder_item_id",
        "line_item_id",
        "item_order",
        "item_id",
        "item_name",
        "sku",
        "description",
        "quantity",
        "unit",
        "rate",
        "bcy_rate",
        "discount",
        "tax_name",
        "tax_percentage",
        "item_total",
        "location_id",
        "location_name",
        "created_time",
        "last_modified_time"
    ]);

    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> Bills = Header(
    [
        "bill_id",
        "bill_number",
        "vendor_id",
        "vendor_name",
        "date",
        "due_date",
        "status",
        "current_sub_status",
        "reference_number",
        "currency_id",
        "currency_code",
        "exchange_rate",
        "total",
        "balance",
        "created_time",
        "last_modified_time"
    ]);

    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> BillLines = Lines(
    [
        "bill_id",
        "bill_number",
        "bill_date",
        "due_date",
        "bill_status",
        "vendor_id",
        "vendor_name",
        "reference_number",
        "currency_code",
        "exchange_rate",
        "purchaseorder_id",
        "purchaseorder_number",
        "purchaseorder_item_id",
        "receive_id",
        "receive_number",
        "receive_line_item_id",
        "line_item_id",
        "item_order",
        "item_id",
        "item_name",
        "sku",
        "description",
        "account_name",
        "quantity",
        "unit",
        "rate",
        "bcy_rate",
        "tax_name",
        "tax_percentage",
        "item_total",
        "location_id",
        "location_name",
        "created_time",
        "last_modified_time"
    ]);

    /// <summary>A contact person's custom fields are its own (<c>cf_…</c>); the customer's are not repeated.</summary>
    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> ContactPersons = Header(
    [
        "contact_person_id",
        "customer_id",
        "customer_name",
        "company_name",
        "customer_status",
        "salutation",
        "first_name",
        "last_name",
        "full_name",
        "email",
        "phone",
        "mobile",
        "designation",
        "department",
        "is_primary_contact",
        "is_added_in_portal"
    ]);

    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> Salespersons = InMemoryQueryFieldsBuilder.Build(
        "salesperson_id",
        "salesperson_name",
        "email",
        "mobile",
        "is_active");

    private static readonly string[] StockItemColumns =
    [
        "item_id",
        "item_name",
        "sku",
        "unit",
        "status",
        "item_type",
        "product_type",
        "category_name",
        "brand",
        "last_modified_time"
    ];

    /// <summary>Stock rows carry no custom fields; the item's are on <see cref="Items"/>.</summary>
    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> InventoryByItem = InMemoryQueryFieldsBuilder.Build(
    [
        .. StockItemColumns,
        "reorder_level",
        "preferred_vendor_id",
        "preferred_vendor_name",
        "stock_on_hand",
        "available_stock",
        "actual_available_stock",
        "committed_stock",
        "actual_committed_stock",
        "available_for_sale_stock",
        "actual_available_for_sale_stock"
    ]);

    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> InventoryByLocation = InMemoryQueryFieldsBuilder.Build(
    [
        .. StockItemColumns,
        "location_id",
        "location_name",
        "location_status",
        "is_primary_location",
        "stock_on_hand",
        "available_stock",
        "actual_available_stock"
    ]);

    private static IReadOnlyDictionary<string, InMemoryQueryField> Header(string[] names)
    {
        return ZohoCustomFields.Allow(InMemoryQueryFieldsBuilder.Build(names), ZohoCustomFields.DocumentPrefix);
    }

    private static IReadOnlyDictionary<string, InMemoryQueryField> Lines(string[] names)
    {
        return ZohoCustomFields.Allow(
            InMemoryQueryFieldsBuilder.Build(names),
            ZohoCustomFields.DocumentPrefix,
            ZohoCustomFields.LinePrefix);
    }
}
