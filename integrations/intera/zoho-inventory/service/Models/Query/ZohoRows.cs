using Intera.ZohoInventory.Api.Infrastructure;
using System.Text.Json;
using static Intera.ZohoInventory.Api.Infrastructure.ZohoJson;

namespace Intera.ZohoInventory.Api.Models.Query;

/// <summary>
/// Turns Zoho Inventory records into query-DSL rows. Every key written here must exist in the
/// matching allowlist in <see cref="ZohoQueryFields"/>, which sits beside this file for that reason:
/// a key with no allowlist entry is a column nobody can filter on, and an allowlist entry with no key
/// is a column that silently reads null.
/// </summary>
/// <remarks>
/// <para>
/// <b>Header sources read the list endpoint only; line sources read each document.</b> A Zoho list
/// carries a document's headline fields but none of its lines, so a line row costs one detail call
/// per document. The header sources stay list-only on purpose: they are the targets of the plugin's
/// lookups (<c>customer_id → customer_name</c>, <c>salesorder_id → salesorder_number</c>), which Core
/// queries every time it labels a filter value, and a lookup that fanned out over every document
/// would spend the organization's daily allowance on labels.
/// </para>
/// <para>
/// <b>Document totals are not repeated on line rows.</b> At line grain a <c>sum(total)</c> would
/// multiply each document by its line count and look plausible. The line's own <c>item_total</c>
/// adds up correctly, and the number of documents is <c>countDistinct(salesorder_id)</c>.
/// </para>
/// <para>
/// <b>A line's location falls back to its document's.</b> Zoho writes <c>location_id</c> on the line
/// only when the organization assigns locations per line; otherwise the whole document ships from or
/// receives into the header's location, and that is the line's location too.
/// </para>
/// <para>
/// Ids are emitted as strings. Zoho's ids are 16-19 digit numbers, past what a JSON consumer can hold
/// in a double, and a caller filtering on one sends it as text anyway.
/// </para>
/// </remarks>
public static class ZohoRows
{
    // ----- Contacts -----

    public static Dictionary<string, object?> Customer(JsonElement contact)
    {
        var row = NewRow();
        row["customer_id"] = Text(contact, "contact_id");
        row["customer_name"] = Text(contact, "contact_name");
        AddContactColumns(row, contact);
        row["outstanding_receivable_amount"] = Number(contact, "outstanding_receivable_amount");
        row["unused_credits_receivable_amount"] = Number(contact, "unused_credits_receivable_amount");
        ZohoCustomFields.AddTo(row, contact, "custom_fields", ZohoCustomFields.DocumentPrefix);
        return row;
    }

    public static Dictionary<string, object?> Vendor(JsonElement contact)
    {
        var row = NewRow();
        row["vendor_id"] = Text(contact, "contact_id");
        row["vendor_name"] = Text(contact, "contact_name");
        AddContactColumns(row, contact);
        row["outstanding_payable_amount"] = Number(contact, "outstanding_payable_amount");
        row["unused_credits_payable_amount"] = Number(contact, "unused_credits_payable_amount");
        ZohoCustomFields.AddTo(row, contact, "custom_fields", ZohoCustomFields.DocumentPrefix);
        return row;
    }

    private static void AddContactColumns(Dictionary<string, object?> row, JsonElement contact)
    {
        row["company_name"] = Text(contact, "company_name");
        row["website"] = Text(contact, "website");
        row["contact_type"] = Text(contact, "contact_type");
        row["status"] = Text(contact, "status");
        row["currency_id"] = Text(contact, "currency_id");
        row["currency_code"] = Text(contact, "currency_code");
        row["payment_terms"] = Number(contact, "payment_terms");
        row["payment_terms_label"] = Text(contact, "payment_terms_label");
        row["first_name"] = Text(contact, "first_name");
        row["last_name"] = Text(contact, "last_name");
        row["email"] = Text(contact, "email");
        row["phone"] = Text(contact, "phone");
        row["mobile"] = Text(contact, "mobile");
        row["created_time"] = Timestamp(contact, "created_time");
        row["last_modified_time"] = Timestamp(contact, "last_modified_time");
    }

    // ----- Items -----

    public static Dictionary<string, object?> Item(JsonElement item)
    {
        var row = NewRow();
        row["item_id"] = Text(item, "item_id");
        row["item_name"] = Text(item, "name");
        row["sku"] = Text(item, "sku");
        row["description"] = Text(item, "description");
        row["status"] = Text(item, "status");
        row["item_type"] = Text(item, "item_type");
        row["product_type"] = Text(item, "product_type");
        row["unit"] = Text(item, "unit");
        row["group_id"] = Text(item, "group_id");
        row["group_name"] = Text(item, "group_name");
        row["category_id"] = Text(item, "category_id");
        row["category_name"] = Text(item, "category_name");
        row["brand"] = Text(item, "brand");
        row["manufacturer"] = Text(item, "manufacturer");
        row["sales_rate"] = Number(item, "rate");
        row["purchase_rate"] = Number(item, "purchase_rate");
        row["tax_name"] = Text(item, "tax_name");
        row["tax_percentage"] = Number(item, "tax_percentage");
        row["is_combo_product"] = Bool(item, "is_combo_product");
        row["can_be_sold"] = Bool(item, "can_be_sold");
        row["can_be_purchased"] = Bool(item, "can_be_purchased");
        row["track_inventory"] = Bool(item, "track_inventory");
        row["is_returnable"] = Bool(item, "is_returnable");
        // The list names the preferred vendor but carries no id for it.
        row["preferred_vendor_name"] = Text(item, "vendor_name");
        row["reorder_level"] = Number(item, "reorder_level");
        row["stock_on_hand"] = Number(item, "stock_on_hand");
        row["available_stock"] = Number(item, "available_stock");
        row["available_for_sale"] = Number(item, "available_for_sale");
        row["actual_available_stock"] = Number(item, "actual_available_stock");
        row["upc"] = Text(item, "upc");
        row["ean"] = Text(item, "ean");
        row["isbn"] = Text(item, "isbn");
        row["part_number"] = Text(item, "part_number");
        row["created_time"] = Timestamp(item, "created_time");
        row["last_modified_time"] = Timestamp(item, "last_modified_time");
        ZohoCustomFields.AddTo(row, item, "custom_fields", ZohoCustomFields.DocumentPrefix);
        return row;
    }

    // ----- Locations -----

    public static Dictionary<string, object?> Location(JsonElement location)
    {
        var row = NewRow();
        row["location_id"] = Text(location, "location_id");
        row["location_name"] = Text(location, "location_name");
        row["type"] = Text(location, "type");
        row["parent_location_id"] = Text(location, "parent_location_id");
        row["is_primary"] = Bool(location, "is_primary_location") ?? Bool(location, "is_primary");
        row["is_active"] = Bool(location, "is_location_active") ?? ActiveFromStatus(Text(location, "status"));
        row["email"] = Text(location, "email");
        row["phone"] = Text(location, "phone");
        row["address"] = FirstText(Path(location, "address") ?? default, "street_address1", "address");
        row["address2"] = FirstText(Path(location, "address") ?? default, "street_address2", "street2");
        row["city"] = Text(Path(location, "address") ?? default, "city");
        row["state"] = Text(Path(location, "address") ?? default, "state");
        row["state_code"] = Text(Path(location, "address") ?? default, "state_code");
        row["postal_code"] = FirstText(Path(location, "address") ?? default, "zip", "postal_code");
        row["country"] = Text(Path(location, "address") ?? default, "country");
        return row;
    }

    private static bool? ActiveFromStatus(string? status)
    {
        return status is null ? null : string.Equals(status, "active", StringComparison.OrdinalIgnoreCase);
    }

    // ----- Sales orders -----

    public static Dictionary<string, object?> SalesOrder(JsonElement order)
    {
        var row = NewRow();
        row["salesorder_id"] = Text(order, "salesorder_id");
        row["salesorder_number"] = Text(order, "salesorder_number");
        row["customer_id"] = Text(order, "customer_id");
        row["customer_name"] = Text(order, "customer_name");
        row["date"] = Date(order, "date");
        row["shipment_date"] = Date(order, "shipment_date");
        row["delivery_date"] = Date(order, "delivery_date");
        row["status"] = Text(order, "status");
        row["order_status"] = Text(order, "order_status");
        row["current_sub_status"] = Text(order, "current_sub_status");
        row["invoiced_status"] = Text(order, "invoiced_status");
        row["paid_status"] = Text(order, "paid_status");
        row["shipped_status"] = Text(order, "shipped_status");
        row["reference_number"] = Text(order, "reference_number");
        row["salesperson_name"] = Text(order, "salesperson_name");
        row["delivery_method"] = Text(order, "delivery_method");
        row["sales_channel"] = Text(order, "sales_channel");
        row["currency_id"] = Text(order, "currency_id");
        row["currency_code"] = Text(order, "currency_code");
        row["quantity"] = Number(order, "quantity");
        row["quantity_invoiced"] = Number(order, "quantity_invoiced");
        row["quantity_packed"] = Number(order, "quantity_packed");
        row["quantity_shipped"] = Number(order, "quantity_shipped");
        row["total"] = Number(order, "total");
        row["bcy_total"] = Number(order, "bcy_total");
        row["total_invoiced_amount"] = Number(order, "total_invoiced_amount");
        row["balance"] = Number(order, "balance");
        row["is_drop_shipment"] = Bool(order, "is_drop_shipment");
        row["is_backorder"] = Bool(order, "is_backorder");
        row["is_emailed"] = Bool(order, "is_emailed");
        row["created_time"] = Timestamp(order, "created_time");
        row["last_modified_time"] = Timestamp(order, "last_modified_time");
        ZohoCustomFields.AddTo(row, order, "custom_fields", ZohoCustomFields.DocumentPrefix);
        return row;
    }

    public static IEnumerable<Dictionary<string, object?>> SalesOrderLines(JsonElement order)
    {
        foreach (var line in Array(order, "line_items"))
        {
            var row = NewRow();
            row["salesorder_id"] = Text(order, "salesorder_id");
            row["salesorder_number"] = Text(order, "salesorder_number");
            row["customer_id"] = Text(order, "customer_id");
            row["customer_name"] = Text(order, "customer_name");
            row["order_date"] = Date(order, "date");
            row["shipment_date"] = Date(order, "shipment_date");
            row["order_status"] = Text(order, "status");
            row["reference_number"] = Text(order, "reference_number");
            row["salesperson_id"] = Text(order, "salesperson_id");
            row["salesperson_name"] = Text(order, "salesperson_name");
            row["currency_code"] = Text(order, "currency_code");
            row["exchange_rate"] = Number(order, "exchange_rate");
            row["line_item_id"] = Text(line, "line_item_id");
            row["item_order"] = Number(line, "item_order");
            row["item_id"] = Text(line, "item_id");
            row["item_name"] = Text(line, "name");
            row["sku"] = Text(line, "sku");
            row["description"] = Text(line, "description");
            row["quantity"] = Number(line, "quantity");
            row["quantity_invoiced"] = Number(line, "quantity_invoiced");
            row["quantity_packed"] = Number(line, "quantity_packed");
            row["quantity_shipped"] = Number(line, "quantity_shipped");
            row["quantity_delivered"] = Number(line, "quantity_delivered");
            row["quantity_cancelled"] = Number(line, "quantity_cancelled");
            row["quantity_returned"] = Number(line, "quantity_returned");
            row["unit"] = Text(line, "unit");
            row["rate"] = Number(line, "rate");
            row["bcy_rate"] = Number(line, "bcy_rate");
            row["tax_name"] = Text(line, "tax_name");
            row["tax_percentage"] = Number(line, "tax_percentage");
            row["item_total"] = Number(line, "item_total");
            row["is_invoiced"] = Bool(line, "is_invoiced");
            row["is_combo_product"] = Bool(line, "is_combo_product");
            row["combo_type"] = Text(line, "combo_type");
            AddLineLocation(row, line, order);
            row["created_time"] = Timestamp(order, "created_time");
            row["last_modified_time"] = Timestamp(order, "last_modified_time");
            ZohoCustomFields.AddTo(row, order, "custom_fields", ZohoCustomFields.DocumentPrefix);
            ZohoCustomFields.AddTo(row, line, "item_custom_fields", ZohoCustomFields.LinePrefix);
            yield return row;
        }
    }

    // ----- Purchase orders -----

    public static Dictionary<string, object?> PurchaseOrder(JsonElement order)
    {
        var row = NewRow();
        row["purchaseorder_id"] = Text(order, "purchaseorder_id");
        row["purchaseorder_number"] = Text(order, "purchaseorder_number");
        row["vendor_id"] = Text(order, "vendor_id");
        row["vendor_name"] = Text(order, "vendor_name");
        row["date"] = Date(order, "date");
        row["delivery_date"] = Date(order, "delivery_date");
        row["expected_delivery_date"] = Date(order, "expected_delivery_date");
        row["status"] = Text(order, "status");
        row["order_status"] = Text(order, "order_status");
        row["received_status"] = Text(order, "received_status");
        row["billed_status"] = Text(order, "billed_status");
        row["reference_number"] = Text(order, "reference_number");
        row["currency_id"] = Text(order, "currency_id");
        row["currency_code"] = Text(order, "currency_code");
        row["total"] = Number(order, "total");
        row["total_ordered_quantity"] = Number(order, "total_ordered_quantity");
        row["quantity_yet_to_receive"] = Number(order, "quantity_yet_to_receive");
        row["quantity_marked_as_received"] = Number(order, "quantity_marked_as_received");
        row["is_po_marked_as_received"] = Bool(order, "is_po_marked_as_received");
        row["is_drop_shipment"] = Bool(order, "is_drop_shipment");
        row["is_backorder"] = Bool(order, "is_backorder");
        row["created_time"] = Timestamp(order, "created_time");
        row["last_modified_time"] = Timestamp(order, "last_modified_time");
        ZohoCustomFields.AddTo(row, order, "custom_fields", ZohoCustomFields.DocumentPrefix);
        return row;
    }

    public static IEnumerable<Dictionary<string, object?>> PurchaseOrderLines(JsonElement order)
    {
        foreach (var line in Array(order, "line_items"))
        {
            var row = NewRow();
            row["purchaseorder_id"] = Text(order, "purchaseorder_id");
            row["purchaseorder_number"] = Text(order, "purchaseorder_number");
            row["vendor_id"] = Text(order, "vendor_id");
            row["vendor_name"] = Text(order, "vendor_name");
            row["order_date"] = Date(order, "date");
            row["delivery_date"] = Date(order, "delivery_date");
            row["expected_delivery_date"] = Date(order, "expected_delivery_date");
            row["po_status"] = Text(order, "status");
            row["reference_number"] = Text(order, "reference_number");
            row["salesorder_id"] = Text(order, "salesorder_id");
            row["currency_code"] = Text(order, "currency_code");
            row["exchange_rate"] = Number(order, "exchange_rate");
            row["line_item_id"] = Text(line, "line_item_id");
            row["item_order"] = Number(line, "item_order");
            row["item_id"] = Text(line, "item_id");
            row["item_name"] = Text(line, "name");
            row["sku"] = Text(line, "sku");
            row["description"] = Text(line, "description");
            row["salesorder_item_id"] = Text(line, "salesorder_item_id");
            row["quantity"] = Number(line, "quantity");
            row["quantity_received"] = Number(line, "quantity_received");
            row["quantity_billed"] = Number(line, "quantity_billed");
            row["quantity_cancelled"] = Number(line, "quantity_cancelled");
            row["unit"] = Text(line, "unit");
            // Measured: a purchase order line carries its price as `rate`. The OpenAPI calls it
            // `purchase_rate`, which the live response does not have.
            row["purchase_rate"] = Number(line, "rate") ?? Number(line, "purchase_rate");
            row["bcy_rate"] = Number(line, "bcy_rate");
            row["tax_name"] = Text(line, "tax_name");
            row["tax_percentage"] = Number(line, "tax_percentage");
            row["item_total"] = Number(line, "item_total");
            AddLineLocation(row, line, order);
            row["created_time"] = Timestamp(order, "created_time");
            row["last_modified_time"] = Timestamp(order, "last_modified_time");
            ZohoCustomFields.AddTo(row, order, "custom_fields", ZohoCustomFields.DocumentPrefix);
            ZohoCustomFields.AddTo(row, line, "item_custom_fields", ZohoCustomFields.LinePrefix);
            yield return row;
        }
    }

    // ----- Purchase receives -----

    public static Dictionary<string, object?> PurchaseReceive(JsonElement receive)
    {
        var row = NewRow();
        row["receive_id"] = FirstText(receive, "purchasereceive_id", "receive_id");
        row["receive_number"] = FirstText(receive, "purchasereceive_number", "receive_number");
        row["date"] = Date(receive, "receive_date") ?? Date(receive, "date");
        row["status"] = Text(receive, "status");
        row["received_status"] = Text(receive, "received_status");
        row["billed_status"] = Text(receive, "billed_status");
        row["purchaseorder_id"] = Text(receive, "purchaseorder_id");
        row["purchaseorder_number"] = Text(receive, "purchaseorder_number");
        row["vendor_id"] = Text(receive, "vendor_id");
        row["vendor_name"] = Text(receive, "vendor_name");
        row["quantity_received"] = Number(receive, "quantity_received") ?? Number(receive, "total_quantity_received");
        row["location_id"] = Text(receive, "location_id");
        row["location_name"] = Text(receive, "location_name");
        row["is_salesreturn"] = Bool(receive, "is_salesreturn");
        row["created_time"] = Timestamp(receive, "created_time");
        row["last_modified_time"] = Timestamp(receive, "last_modified_time");
        ZohoCustomFields.AddTo(row, receive, "custom_fields", ZohoCustomFields.DocumentPrefix);
        return row;
    }

    public static IEnumerable<Dictionary<string, object?>> PurchaseReceiveLines(JsonElement receive)
    {
        foreach (var line in Array(receive, "line_items"))
        {
            var row = NewRow();
            row["receive_id"] = FirstText(receive, "receive_id", "purchasereceive_id");
            row["receive_number"] = FirstText(receive, "receive_number", "purchasereceive_number");
            row["date"] = Date(receive, "date") ?? Date(receive, "receive_date");
            row["status"] = Text(receive, "status");
            row["purchaseorder_id"] = Text(receive, "purchaseorder_id");
            row["purchaseorder_number"] = Text(receive, "purchaseorder_number");
            row["vendor_id"] = Text(receive, "vendor_id");
            row["vendor_name"] = Text(receive, "vendor_name");
            row["line_item_id"] = Text(line, "line_item_id");
            row["item_order"] = Number(line, "item_order");
            row["item_id"] = Text(line, "item_id");
            row["item_name"] = Text(line, "name");
            row["sku"] = Text(line, "sku");
            row["description"] = Text(line, "description");
            row["quantity_received"] = Number(line, "quantity") ?? Number(line, "quantity_received");
            row["quantity_billed"] = Number(line, "quantity_billed");
            row["unit"] = Text(line, "unit");
            AddLineLocation(row, line, receive);
            row["is_billed"] = Bool(receive, "is_billed");
            row["created_time"] = Timestamp(receive, "created_time");
            row["last_modified_time"] = Timestamp(receive, "last_modified_time");
            ZohoCustomFields.AddTo(row, receive, "custom_fields", ZohoCustomFields.DocumentPrefix);
            ZohoCustomFields.AddTo(row, line, "item_custom_fields", ZohoCustomFields.LinePrefix);
            yield return row;
        }
    }

    // ----- Packages and shipments -----

    public static Dictionary<string, object?> Package(JsonElement package)
    {
        var row = NewRow();
        row["package_id"] = Text(package, "package_id");
        row["package_number"] = Text(package, "package_number");
        row["salesorder_id"] = Text(package, "salesorder_id");
        row["salesorder_number"] = Text(package, "salesorder_number");
        row["customer_id"] = Text(package, "customer_id");
        row["customer_name"] = Text(package, "customer_name");
        row["date"] = Date(package, "date");
        row["status"] = Text(package, "status");
        row["shipment_id"] = Text(package, "shipment_id");
        row["shipment_number"] = Text(package, "shipment_number");
        row["shipment_date"] = Date(package, "shipment_date");
        row["delivery_method"] = Text(package, "delivery_method");
        row["tracking_number"] = Text(package, "tracking_number");
        row["total_quantity"] = Number(package, "total_quantity");
        row["created_time"] = Timestamp(package, "created_time");
        row["last_modified_time"] = Timestamp(package, "last_modified_time");
        ZohoCustomFields.AddTo(row, package, "custom_fields", ZohoCustomFields.DocumentPrefix);
        return row;
    }

    /// <summary>
    /// Package lines with their shipment on them. <c>package_lines</c> and <c>shipment_lines</c> are
    /// both this row: a shipment line is a package line whose package has been shipped, which is also
    /// the only place Zoho states a shipped quantity (its shipment object's own lines carry no
    /// quantity at all).
    /// </summary>
    public static IEnumerable<Dictionary<string, object?>> PackageLines(JsonElement package)
    {
        var shipment = Path(package, "shipment_order");

        foreach (var line in Array(package, "line_items"))
        {
            var row = NewRow();
            row["package_id"] = Text(package, "package_id");
            row["package_number"] = Text(package, "package_number");
            row["package_date"] = Date(package, "date");
            row["package_status"] = Text(package, "status");
            row["salesorder_id"] = Text(package, "salesorder_id");
            row["salesorder_number"] = Text(package, "salesorder_number");
            row["customer_id"] = Text(package, "customer_id");
            row["customer_name"] = Text(package, "customer_name");
            row["line_item_id"] = Text(line, "line_item_id");
            row["salesorder_line_item_id"] = Text(line, "so_line_item_id");
            row["item_order"] = Number(line, "item_order");
            row["item_id"] = Text(line, "item_id");
            row["item_name"] = Text(line, "name");
            row["sku"] = Text(line, "sku");
            row["description"] = Text(line, "description");
            row["quantity"] = Number(line, "quantity");
            row["unit"] = Text(line, "unit");
            row["is_invoiced"] = Bool(line, "is_invoiced");
            AddShipmentColumns(row, shipment);
            row["created_time"] = Timestamp(package, "created_time");
            row["last_modified_time"] = Timestamp(package, "last_modified_time");
            ZohoCustomFields.AddTo(row, package, "custom_fields", ZohoCustomFields.DocumentPrefix);
            ZohoCustomFields.AddTo(row, line, "item_custom_fields", ZohoCustomFields.LinePrefix);
            yield return row;
        }
    }

    private static void AddShipmentColumns(Dictionary<string, object?> row, JsonElement? shipment)
    {
        var value = shipment ?? default;
        row["shipment_id"] = Text(value, "shipment_id");
        row["shipment_number"] = Text(value, "shipment_number");
        row["shipment_date"] = Date(value, "shipping_date") ?? Date(value, "shipment_date") ?? Date(value, "date");
        row["shipment_status"] = Text(value, "status");
        row["carrier"] = Text(value, "carrier");
        row["tracking_number"] = Text(value, "tracking_number");
        row["delivery_method"] = Text(value, "delivery_method");
    }

    /// <summary>
    /// One shipment, read out of the package it was created for. Zoho has no shipment list endpoint
    /// — a shipment order can only be fetched by id — so the package detail, which embeds its
    /// shipment, is where shipments are found.
    /// </summary>
    public static Dictionary<string, object?>? Shipment(JsonElement package)
    {
        if (Path(package, "shipment_order") is not { ValueKind: JsonValueKind.Object } shipment
            || Text(shipment, "shipment_id") is null)
        {
            return null;
        }

        var address = Path(package, "shipping_address") ?? default;

        var row = NewRow();
        row["shipment_id"] = Text(shipment, "shipment_id");
        row["shipment_number"] = Text(shipment, "shipment_number");
        row["salesorder_id"] = Text(package, "salesorder_id");
        row["salesorder_number"] = Text(package, "salesorder_number");
        row["customer_id"] = Text(package, "customer_id");
        row["customer_name"] = Text(package, "customer_name");
        row["shipment_date"] = Date(shipment, "shipping_date") ?? Date(shipment, "shipment_date") ?? Date(shipment, "date");
        row["status"] = Text(shipment, "status");
        row["detailed_status"] = Text(shipment, "detailed_status");
        row["carrier"] = Text(shipment, "carrier");
        row["service"] = Text(shipment, "service");
        row["delivery_method"] = Text(shipment, "delivery_method");
        row["tracking_number"] = Text(shipment, "tracking_number");
        row["shipment_rate"] = Number(shipment, "shipment_rate");
        row["delivery_days"] = Number(shipment, "delivery_days");
        row["delivery_guarantee"] = Bool(shipment, "delivery_guarantee");
        row["notes"] = Text(shipment, "notes");
        row["shipping_address"] = Text(address, "address");
        row["shipping_city"] = Text(address, "city");
        row["shipping_state"] = Text(address, "state");
        row["shipping_postcode"] = Text(address, "zip");
        row["shipping_country"] = Text(address, "country");
        row["package_ids"] = Text(package, "package_id");
        row["package_numbers"] = Text(package, "package_number");
        row["package_count"] = 1m;
        return row;
    }

    /// <summary>
    /// Folds a second package of the same shipment into its row. A shipment order may be created for
    /// several packages of one sales order at once; the row stays one per shipment, so
    /// <c>shipment_rate</c> adds up, and the packages are listed rather than repeated.
    /// </summary>
    public static void MergePackage(Dictionary<string, object?> shipmentRow, JsonElement package)
    {
        ArgumentNullException.ThrowIfNull(shipmentRow);

        shipmentRow["package_ids"] = Append(shipmentRow["package_ids"] as string, Text(package, "package_id"));
        shipmentRow["package_numbers"] = Append(shipmentRow["package_numbers"] as string, Text(package, "package_number"));
        shipmentRow["package_count"] = (shipmentRow["package_count"] as decimal? ?? 0m) + 1m;
    }

    private static string? Append(string? list, string? value)
    {
        return value is null ? list : list is null ? value : $"{list}, {value}";
    }

    // ----- Invoices -----

    /// <remarks>
    /// Columns as documented: the test organization has no invoices, so unlike every other list here
    /// this one has not been compared with a live response.
    /// </remarks>
    public static Dictionary<string, object?> Invoice(JsonElement invoice)
    {
        var row = NewRow();
        row["invoice_id"] = Text(invoice, "invoice_id");
        row["invoice_number"] = Text(invoice, "invoice_number");
        row["customer_id"] = Text(invoice, "customer_id");
        row["customer_name"] = Text(invoice, "customer_name");
        row["date"] = Date(invoice, "date");
        row["due_date"] = Date(invoice, "due_date");
        row["status"] = Text(invoice, "status");
        row["reference_number"] = Text(invoice, "reference_number");
        row["salesperson_id"] = Text(invoice, "salesperson_id");
        row["salesperson_name"] = Text(invoice, "salesperson_name");
        row["currency_id"] = Text(invoice, "currency_id");
        row["currency_code"] = Text(invoice, "currency_code");
        row["exchange_rate"] = Number(invoice, "exchange_rate");
        row["total"] = Number(invoice, "total");
        row["balance"] = Number(invoice, "balance");
        row["write_off_amount"] = Number(invoice, "write_off_amount");
        row["payment_expected_date"] = Date(invoice, "payment_expected_date");
        row["last_payment_date"] = Date(invoice, "last_payment_date");
        row["location_id"] = Text(invoice, "location_id");
        row["location_name"] = Text(invoice, "location_name");
        row["is_emailed"] = Bool(invoice, "is_emailed");
        row["is_viewed_by_client"] = Bool(invoice, "is_viewed_by_client");
        row["created_time"] = Timestamp(invoice, "created_time");
        row["last_modified_time"] = Timestamp(invoice, "last_modified_time");
        ZohoCustomFields.AddTo(row, invoice, "custom_fields", ZohoCustomFields.DocumentPrefix);
        return row;
    }

    /// <remarks>
    /// <c>salesorder_id</c>, <c>salesorder_number</c> and <c>salesorder_item_id</c> are not in the
    /// documented invoice schema; they are read because they are what "ordered against invoiced" is
    /// matched on, and have not been seen with data (the test organization has no invoices).
    /// </remarks>
    public static IEnumerable<Dictionary<string, object?>> InvoiceLines(JsonElement invoice)
    {
        foreach (var line in Array(invoice, "line_items"))
        {
            var row = NewRow();
            row["invoice_id"] = Text(invoice, "invoice_id");
            row["invoice_number"] = Text(invoice, "invoice_number");
            row["customer_id"] = Text(invoice, "customer_id");
            row["customer_name"] = Text(invoice, "customer_name");
            row["invoice_date"] = Date(invoice, "date");
            row["due_date"] = Date(invoice, "due_date");
            row["invoice_status"] = Text(invoice, "status");
            row["reference_number"] = Text(invoice, "reference_number");
            row["salesperson_id"] = Text(invoice, "salesperson_id");
            row["salesperson_name"] = Text(invoice, "salesperson_name");
            row["currency_code"] = Text(invoice, "currency_code");
            row["exchange_rate"] = Number(invoice, "exchange_rate");
            row["salesorder_id"] = Text(line, "salesorder_id") ?? Text(invoice, "salesorder_id");
            row["salesorder_number"] = Text(line, "salesorder_number") ?? Text(invoice, "salesorder_number");
            row["salesorder_item_id"] = Text(line, "salesorder_item_id");
            row["line_item_id"] = Text(line, "line_item_id");
            row["item_order"] = Number(line, "item_order");
            row["item_id"] = Text(line, "item_id");
            row["item_name"] = Text(line, "name");
            row["sku"] = Text(line, "sku");
            row["description"] = Text(line, "description");
            row["quantity"] = Number(line, "quantity");
            row["unit"] = Text(line, "unit");
            row["rate"] = Number(line, "rate");
            row["bcy_rate"] = Number(line, "bcy_rate");
            row["discount"] = Number(line, "discount");
            row["tax_name"] = Text(line, "tax_name");
            row["tax_percentage"] = Number(line, "tax_percentage");
            row["item_total"] = Number(line, "item_total");
            AddLineLocation(row, line, invoice);
            row["created_time"] = Timestamp(invoice, "created_time");
            row["last_modified_time"] = Timestamp(invoice, "last_modified_time");
            ZohoCustomFields.AddTo(row, invoice, "custom_fields", ZohoCustomFields.DocumentPrefix);
            ZohoCustomFields.AddTo(row, line, "item_custom_fields", ZohoCustomFields.LinePrefix);
            yield return row;
        }
    }

    // ----- Bills -----

    /// <remarks>
    /// Measured 2026-09-18: the list carries <c>total</c> and <c>current_sub_status</c>, which the
    /// documentation omits, and none of the documented <c>purchaseorder_id</c>, <c>sub_total</c>,
    /// <c>tax_total</c> or location. The purchase order a bill settles is therefore a line column
    /// (a bill may cover several orders, which the detail lists in <c>purchaseorder_ids</c>).
    /// </remarks>
    public static Dictionary<string, object?> Bill(JsonElement bill)
    {
        var row = NewRow();
        row["bill_id"] = Text(bill, "bill_id");
        row["bill_number"] = Text(bill, "bill_number");
        row["vendor_id"] = Text(bill, "vendor_id");
        row["vendor_name"] = Text(bill, "vendor_name");
        row["date"] = Date(bill, "date");
        row["due_date"] = Date(bill, "due_date");
        row["status"] = Text(bill, "status");
        row["current_sub_status"] = Text(bill, "current_sub_status");
        row["reference_number"] = Text(bill, "reference_number");
        row["currency_id"] = Text(bill, "currency_id");
        row["currency_code"] = Text(bill, "currency_code");
        row["exchange_rate"] = Number(bill, "exchange_rate");
        row["total"] = Number(bill, "total");
        row["balance"] = Number(bill, "balance");
        row["created_time"] = Timestamp(bill, "created_time");
        row["last_modified_time"] = Timestamp(bill, "last_modified_time");
        ZohoCustomFields.AddTo(row, bill, "custom_fields", ZohoCustomFields.DocumentPrefix);
        return row;
    }

    /// <remarks>
    /// Measured 2026-09-18: a bill line names the purchase order line it bills
    /// (<c>purchaseorder_item_id</c>, with the order's number under <c>purchaseorder_details</c>) and
    /// the receive line it bills (<c>receive_item_id</c>, which is the receive line's own
    /// <c>line_item_id</c>). That is the chain "ordered → received → billed", so all three ids are
    /// columns. A line billed against no receive carries empty strings, which read as null.
    /// </remarks>
    public static IEnumerable<Dictionary<string, object?>> BillLines(JsonElement bill)
    {
        foreach (var line in Array(bill, "line_items"))
        {
            var receiveId = Text(line, "receive_id");

            var row = NewRow();
            row["bill_id"] = Text(bill, "bill_id");
            row["bill_number"] = Text(bill, "bill_number");
            row["bill_date"] = Date(bill, "date");
            row["due_date"] = Date(bill, "due_date");
            row["bill_status"] = Text(bill, "status");
            row["vendor_id"] = Text(bill, "vendor_id");
            row["vendor_name"] = Text(bill, "vendor_name");
            row["reference_number"] = Text(bill, "reference_number");
            row["currency_code"] = Text(bill, "currency_code");
            row["exchange_rate"] = Number(bill, "exchange_rate");
            row["purchaseorder_id"] = Text(line, "purchaseorder_id");
            row["purchaseorder_number"] = Text(Path(line, "purchaseorder_details") ?? default, "purchaseorder_number");
            row["purchaseorder_item_id"] = Text(line, "purchaseorder_item_id");
            row["receive_id"] = receiveId;
            row["receive_number"] = Array(line, "receive_line_items")
                .Where(receive => receiveId is not null && Text(receive, "receive_id") == receiveId)
                .Select(static receive => Text(receive, "receive_number"))
                .FirstOrDefault(static number => number is not null);
            row["receive_line_item_id"] = Text(line, "receive_item_id");
            row["line_item_id"] = Text(line, "line_item_id");
            row["item_order"] = Number(line, "item_order");
            row["item_id"] = Text(line, "item_id");
            row["item_name"] = Text(line, "name");
            row["sku"] = Text(line, "sku");
            row["description"] = Text(line, "description");
            row["account_name"] = Text(line, "account_name");
            row["quantity"] = Number(line, "quantity");
            row["unit"] = Text(line, "unit");
            row["rate"] = Number(line, "rate");
            row["bcy_rate"] = Number(line, "bcy_rate");
            row["tax_name"] = Text(line, "tax_name");
            row["tax_percentage"] = Number(line, "tax_percentage");
            row["item_total"] = Number(line, "item_total");
            AddLineLocation(row, line, bill);
            row["created_time"] = Timestamp(bill, "created_time");
            row["last_modified_time"] = Timestamp(bill, "last_modified_time");
            ZohoCustomFields.AddTo(row, bill, "custom_fields", ZohoCustomFields.DocumentPrefix);
            ZohoCustomFields.AddTo(row, line, "item_custom_fields", ZohoCustomFields.LinePrefix);
            yield return row;
        }
    }

    // ----- Contact persons -----

    /// <remarks>
    /// Measured 2026-09-18: the list answers without the documented <c>communication_preference</c>,
    /// so there are no e-mail/SMS/WhatsApp flags to expose.
    /// </remarks>
    public static Dictionary<string, object?> ContactPerson(JsonElement customer, JsonElement person)
    {
        var firstName = Text(person, "first_name");
        var lastName = Text(person, "last_name");

        var row = NewRow();
        row["contact_person_id"] = Text(person, "contact_person_id");
        row["customer_id"] = Text(customer, "contact_id");
        row["customer_name"] = Text(customer, "contact_name");
        row["company_name"] = Text(customer, "company_name");
        row["customer_status"] = Text(customer, "status");
        row["salutation"] = Text(person, "salutation");
        row["first_name"] = firstName;
        row["last_name"] = lastName;
        row["full_name"] = firstName is null ? lastName : lastName is null ? firstName : $"{firstName} {lastName}";
        row["email"] = Text(person, "email");
        row["phone"] = Text(person, "phone");
        row["mobile"] = Text(person, "mobile");
        row["designation"] = Text(person, "designation");
        row["department"] = Text(person, "department");
        row["is_primary_contact"] = Bool(person, "is_primary_contact");
        row["is_added_in_portal"] = Bool(person, "is_added_in_portal");
        ZohoCustomFields.AddTo(row, person, "contactperson_custom_fields", ZohoCustomFields.DocumentPrefix);
        return row;
    }

    // ----- Salespersons -----

    public static Dictionary<string, object?> Salesperson(JsonElement salesperson)
    {
        var row = NewRow();
        row["salesperson_id"] = Text(salesperson, "salesperson_id");
        row["salesperson_name"] = Text(salesperson, "salesperson_name");
        row["email"] = Text(salesperson, "salesperson_email");
        row["mobile"] = Text(salesperson, "salesperson_mobile");
        row["is_active"] = Bool(salesperson, "is_active");
        return row;
    }

    // ----- Stock -----

    /// <summary>
    /// An item's stock, organization-wide: identity from the list entry the filter was narrowed
    /// against, stock and preferred vendor from its <c>/itemdetails</c> record.
    /// </summary>
    /// <remarks>
    /// Zoho keeps two ledgers and both are exposed under its own names, not reconciled here:
    /// <c>stock_on_hand</c>/<c>available_stock</c> are the accounting stock (moved by bills and
    /// invoices), the <c>actual_…</c> columns the physical stock (moved by receives and shipments).
    /// Measured 2026-09-18: an item billed for 10 and not yet received reads
    /// <c>available_stock = 10</c> and <c>actual_available_stock = 0</c>, with 5 committed to a sales
    /// order. <c>committed_stock</c> and the <c>…_for_sale_stock</c> pair are in no documented
    /// response and are in this one.
    /// </remarks>
    public static Dictionary<string, object?> InventoryByItem(JsonElement item, JsonElement detail)
    {
        var row = NewRow();
        AddStockItemColumns(row, item);
        row["reorder_level"] = Number(detail, "reorder_level") ?? Number(item, "reorder_level");
        row["preferred_vendor_id"] = Text(detail, "vendor_id");
        row["preferred_vendor_name"] = Text(detail, "vendor_name") ?? Text(item, "vendor_name");
        row["stock_on_hand"] = Number(detail, "stock_on_hand");
        row["available_stock"] = Number(detail, "available_stock");
        row["actual_available_stock"] = Number(detail, "actual_available_stock");
        row["committed_stock"] = Number(detail, "committed_stock");
        row["actual_committed_stock"] = Number(detail, "actual_committed_stock");
        row["available_for_sale_stock"] = Number(detail, "available_for_sale_stock");
        row["actual_available_for_sale_stock"] = Number(detail, "actual_available_for_sale_stock");
        return row;
    }

    /// <summary>
    /// An item's stock per location, from the <c>locations</c> array of its <c>/itemdetails</c>
    /// record. Without the array (multi-warehouse off), one row at <paramref name="onlyLocation"/>,
    /// or at a null location when the organization has more than one.
    /// </summary>
    /// <remarks>
    /// Per location Zoho documents only the three stock figures below — no committed stock — so the
    /// fallback row carries the same three, from the organization-wide figures.
    /// </remarks>
    public static IEnumerable<Dictionary<string, object?>> InventoryByLocation(JsonElement item, JsonElement detail, JsonElement? onlyLocation)
    {
        var locations = Array(detail, "locations").ToList();

        if (locations.Count == 0)
        {
            var location = onlyLocation ?? default;

            var row = NewRow();
            AddStockItemColumns(row, item);
            row["location_id"] = Text(location, "location_id");
            row["location_name"] = Text(location, "location_name");
            // The locations list states activity as a flag, where the per-item array has a status.
            row["location_status"] = Text(location, "status")
                ?? (Bool(location, "is_location_active") is { } active ? (active ? "active" : "inactive") : null);
            row["is_primary_location"] = Bool(location, "is_primary_location");
            row["stock_on_hand"] = Number(detail, "stock_on_hand");
            row["available_stock"] = Number(detail, "available_stock");
            row["actual_available_stock"] = Number(detail, "actual_available_stock");
            yield return row;
            yield break;
        }

        foreach (var location in locations)
        {
            var row = NewRow();
            AddStockItemColumns(row, item);
            row["location_id"] = Text(location, "location_id");
            row["location_name"] = Text(location, "location_name");
            row["location_status"] = Text(location, "status");
            row["is_primary_location"] = Bool(location, "is_primary");
            row["stock_on_hand"] = Number(location, "location_stock_on_hand");
            row["available_stock"] = Number(location, "location_available_stock");
            row["actual_available_stock"] = Number(location, "location_actual_available_stock");
            yield return row;
        }
    }

    /// <summary>The item columns a stock row repeats, all from the list entry.</summary>
    private static void AddStockItemColumns(Dictionary<string, object?> row, JsonElement item)
    {
        row["item_id"] = Text(item, "item_id");
        row["item_name"] = Text(item, "name");
        row["sku"] = Text(item, "sku");
        row["unit"] = Text(item, "unit");
        row["status"] = Text(item, "status");
        row["item_type"] = Text(item, "item_type");
        row["product_type"] = Text(item, "product_type");
        row["category_name"] = Text(item, "category_name");
        row["brand"] = Text(item, "brand");
        row["last_modified_time"] = Timestamp(item, "last_modified_time");
    }

    private static void AddLineLocation(Dictionary<string, object?> row, JsonElement line, JsonElement document)
    {
        var lineLocation = Text(line, "location_id") ?? Text(line, "warehouse_id");

        row["location_id"] = lineLocation ?? Text(document, "location_id");
        row["location_name"] = lineLocation is not null
            ? Text(line, "location_name")
            : Text(document, "location_name");
    }

    private static Dictionary<string, object?> NewRow() => new(StringComparer.OrdinalIgnoreCase);
}
