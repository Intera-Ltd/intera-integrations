using Intera.Unleashed.Api.Models;

namespace Intera.Unleashed.Api.Models.Query;

/// <summary>
/// Turns Unleashed objects into query-DSL rows. Every key set here must exist in the matching
/// allowlist in <see cref="UnleashedQueryFields"/>, which sits beside this file for exactly that
/// reason: the names and the values are two halves of one contract, and a key with no allowlist entry
/// is a column nobody can filter on while an allowlist entry with no key is a column that silently
/// reads null.
/// </summary>
/// <remarks>
/// <para>
/// <b>Six of the nine sources are line-grained.</b> A purchase order, a sales order, a shipment, a
/// transfer, a stock count and a supplier return are all documents carrying a collection of lines,
/// and every question the integration exists to answer — incoming stock, committed demand, actual
/// dispatches, counted-versus-system — is a question about an SKU, i.e. about a line. So one row is
/// one line, with the document's own attributes (number, date, status, warehouse, counterparty)
/// repeated on it.
/// </para>
/// <para>
/// <b>The document's monetary totals are deliberately not repeated.</b> <c>SubTotal</c>,
/// <c>TaxTotal</c> and <c>Total</c> belong to the document, so at line grain a
/// <c>sum(order_total)</c> would multiply every order by its own line count and look entirely
/// plausible while being wrong. The line's own <c>line_total</c>/<c>line_tax</c> add up correctly,
/// and "how many orders" is <c>countDistinct(order_number)</c>, which the DSL has.
/// </para>
/// <para>
/// Ids are emitted as strings, not <see cref="System.Guid"/>: the in-memory engine compares text
/// case-insensitively and a caller filtering on an id sends it as a string. An all-zero guid is
/// emitted as null, since that is what Unleashed writes for "no reference" on some objects.
/// </para>
/// </remarks>
public static class UnleashedRows
{
    // ----- Warehouses -----

    public static Dictionary<string, object?> Warehouse(UnleashedWarehouse warehouse)
    {
        ArgumentNullException.ThrowIfNull(warehouse);

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["warehouse_id"] = Id(warehouse.Id),
            ["warehouse_code"] = Text(warehouse.WarehouseCode),
            ["warehouse_name"] = Text(warehouse.WarehouseName),
            ["is_default"] = warehouse.IsDefault,
            ["is_obsolete"] = warehouse.Obsolete,
            ["street_no"] = Text(warehouse.StreetNo),
            ["address_line1"] = Text(warehouse.AddressLine1),
            ["address_line2"] = Text(warehouse.AddressLine2),
            ["city"] = Text(warehouse.City),
            ["region"] = Text(warehouse.Region),
            ["country"] = Text(warehouse.Country),
            ["post_code"] = Text(warehouse.PostCode),
            ["phone_number"] = Text(warehouse.PhoneNumber),
            ["fax_number"] = Text(warehouse.FaxNumber),
            ["mobile_number"] = Text(warehouse.MobileNumber),
            ["ddi_number"] = Text(warehouse.DDINumber),
            ["contact_name"] = Text(warehouse.ContactName),
            ["last_modified_on"] = warehouse.LastModifiedOn
        };
    }

    // ----- Products -----

    public static Dictionary<string, object?> Product(UnleashedProduct product)
    {
        ArgumentNullException.ThrowIfNull(product);

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["product_id"] = Id(product.Id),
            ["product_code"] = Text(product.ProductCode),
            ["product_description"] = Text(product.ProductDescription),
            ["barcode"] = Text(product.Barcode),
            ["product_group"] = Text(product.ProductGroup?.GroupName),
            ["product_sub_group"] = Text(product.ProductSubGroup?.GroupName),
            ["product_brand"] = Text(product.ProductBrand?.BrandName),
            ["unit_of_measure"] = Text(product.UnitOfMeasure?.Name),
            ["purchase_unit_of_measure"] = Text(product.DefaultPurchasesUnitOfMeasure?.Name),
            ["supplier_code"] = Text(product.Supplier?.SupplierCode),
            ["supplier_name"] = Text(product.Supplier?.SupplierName),
            ["pack_size"] = product.PackSize,
            ["width"] = product.Width,
            ["height"] = product.Height,
            ["depth"] = product.Depth,
            ["weight"] = product.Weight,
            ["min_stock_alert_level"] = product.MinStockAlertLevel,
            ["max_stock_alert_level"] = product.MaxStockAlertLevel,
            ["last_cost"] = product.LastCost,
            ["default_purchase_price"] = product.DefaultPurchasePrice,
            ["default_sell_price"] = product.DefaultSellPrice,
            ["average_land_price"] = product.AverageLandPrice,
            ["nominal_cost"] = product.NominalCost,
            ["minimum_sell_price"] = product.MinimumSellPrice,
            ["minimum_sale_quantity"] = product.MinimumSaleQuantity,
            ["minimum_order_quantity"] = product.MinimumOrderQuantity,
            ["is_obsolete"] = product.Obsolete,
            ["never_diminishing"] = product.NeverDiminishing,
            ["is_component"] = product.IsComponent,
            ["is_assembled_product"] = product.IsAssembledProduct,
            ["is_serialized"] = product.IsSerialized,
            ["is_batch_tracked"] = product.IsBatchTracked,
            ["is_purchasable"] = product.IsPurchasable,
            ["is_sellable"] = product.IsSellable,
            ["bin_location"] = Text(product.BinLocation),
            ["notes"] = Text(product.Notes),
            ["created_on"] = product.CreatedOn,
            ["created_by"] = Text(product.CreatedBy),
            ["last_modified_on"] = product.LastModifiedOn,
            ["last_modified_by"] = Text(product.LastModifiedBy)
        };
    }

    // ----- Stock on hand -----

    public static Dictionary<string, object?> StockOnHand(UnleashedStockOnHand stock)
    {
        ArgumentNullException.ThrowIfNull(stock);

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["stock_on_hand_id"] = Id(stock.Id),
            ["product_id"] = Id(stock.ProductGuid),
            ["product_code"] = Text(stock.ProductCode),
            ["product_description"] = Text(stock.ProductDescription),
            ["product_group"] = Text(stock.ProductGroupName),
            ["warehouse_id"] = Id(stock.WarehouseId),
            ["warehouse_code"] = Text(stock.WarehouseCode),
            ["warehouse_name"] = Text(stock.Warehouse),
            ["qty_on_hand"] = stock.QtyOnHand,
            ["allocated_qty"] = stock.AllocatedQty,
            ["available_qty"] = stock.AvailableQty,
            ["on_purchase"] = stock.OnPurchase,
            ["avg_cost"] = stock.AvgCost,
            ["total_cost"] = stock.TotalCost,
            ["days_since_last_sale"] = stock.DaysSinceLastSale,
            ["last_modified_on"] = stock.LastModifiedOn
        };
    }

    // ----- Purchase order lines -----

    public static IEnumerable<Dictionary<string, object?>> PurchaseOrderLines(UnleashedPurchaseOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);

        foreach (var line in order.PurchaseOrderLines)
        {
            yield return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["order_id"] = Id(order.Id),
                ["order_number"] = Text(order.OrderNumber),
                ["order_date"] = order.OrderDate,
                ["completed_date"] = order.CompletedDate,
                ["received_date"] = order.ReceivedDate,
                ["supplier_invoice_date"] = order.SupplierInvoiceDate,
                ["order_status"] = Text(order.OrderStatus),
                ["custom_order_status"] = Text(order.CustomOrderStatus),
                ["supplier_code"] = Text(order.Supplier?.SupplierCode),
                ["supplier_name"] = Text(order.Supplier?.SupplierName),
                ["supplier_ref"] = Text(order.SupplierRef),
                ["warehouse_code"] = Text(order.Warehouse?.WarehouseCode),
                ["warehouse_name"] = Text(order.Warehouse?.WarehouseName),
                ["currency_code"] = Text(order.Currency?.CurrencyCode),
                ["exchange_rate"] = order.ExchangeRate,
                ["order_comments"] = Text(order.Comments),
                ["created_on"] = order.CreatedOn,
                ["created_by"] = Text(order.CreatedBy),
                ["last_modified_on"] = order.LastModifiedOn,
                ["last_modified_by"] = Text(order.LastModifiedBy),
                ["line_id"] = Id(line.Id),
                ["line_number"] = line.LineNumber,
                ["product_id"] = Id(line.Product?.Id),
                ["product_code"] = Text(line.Product?.ProductCode),
                ["product_description"] = Text(line.Product?.ProductDescription),
                ["product_group"] = Text(line.Product?.ProductGroup?.GroupName),
                ["unit_of_measure"] = Text(line.UnitOfMeasure?.Name),
                ["delivery_date"] = line.DeliveryDate,
                ["order_quantity"] = line.OrderQuantity,
                ["receipt_quantity"] = line.ReceiptQuantity,
                // The one derived column in this project, and it is derived because the obvious
                // spelling in the DSL is wrong. `order_quantity - receipt_quantity` reads null on
                // every line Unleashed has not receipted yet — the engine yields null for a
                // non-numeric operand, and an unreceipted line carries no ReceiptQuantity at all —
                // which is precisely the set of lines "incoming stock" is asking about. Treating the
                // absent receipt as zero is what the column means. It is not clamped at zero: an
                // over-receipt is real and worth seeing as a negative.
                ["outstanding_quantity"] = line.OrderQuantity is null ? null : line.OrderQuantity - (line.ReceiptQuantity ?? 0m),
                ["purchase_unit_order_quantity"] = line.PurchaseUnitOrderQuantity,
                ["purchase_unit_receipt_quantity"] = line.PurchaseUnitReceiptQuantity,
                ["unit_price"] = line.UnitPrice,
                ["discounted_unit_price"] = line.DiscountedUnitPrice,
                ["discount_rate"] = line.DiscountRate,
                ["line_total"] = line.LineTotal,
                ["line_tax"] = line.LineTax,
                ["line_tax_code"] = Text(line.LineTaxCode),
                ["line_tax_rate"] = line.LineTaxRate,
                ["volume"] = line.Volume,
                ["weight"] = line.Weight,
                ["line_comments"] = Text(line.Comments)
            };
        }
    }

    // ----- Sales order lines -----

    public static IEnumerable<Dictionary<string, object?>> SalesOrderLines(UnleashedSalesOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);

        foreach (var line in order.SalesOrderLines)
        {
            yield return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["order_id"] = Id(order.Id),
                ["order_number"] = Text(order.OrderNumber),
                ["order_date"] = order.OrderDate,
                ["required_date"] = order.RequiredDate,
                ["completed_date"] = order.CompletedDate,
                ["received_date"] = order.ReceivedDate,
                ["payment_due_date"] = order.PaymentDueDate,
                ["order_status"] = Text(order.OrderStatus),
                ["custom_order_status"] = Text(order.CustomOrderStatus),
                ["customer_code"] = Text(order.Customer?.CustomerCode),
                ["customer_name"] = Text(order.Customer?.CustomerName),
                ["customer_ref"] = Text(order.CustomerRef),
                ["warehouse_code"] = Text(order.Warehouse?.WarehouseCode),
                ["warehouse_name"] = Text(order.Warehouse?.WarehouseName),
                ["currency_code"] = Text(order.Currency?.CurrencyCode),
                ["exchange_rate"] = order.ExchangeRate,
                ["salesperson_name"] = Text(order.Salesperson?.FullName),
                ["sales_order_group"] = Text(order.SalesOrderGroup),
                ["delivery_method"] = Text(order.DeliveryMethod),
                ["delivery_city"] = Text(order.DeliveryCity),
                ["delivery_region"] = Text(order.DeliveryRegion),
                ["delivery_country"] = Text(order.DeliveryCountry),
                ["delivery_post_code"] = Text(order.DeliveryPostCode),
                ["order_comments"] = Text(order.Comments),
                ["created_on"] = order.CreatedOn,
                ["created_by"] = Text(order.CreatedBy),
                ["last_modified_on"] = order.LastModifiedOn,
                ["last_modified_by"] = Text(order.LastModifiedBy),
                ["line_id"] = Id(line.Id),
                ["line_number"] = line.LineNumber,
                // "Charge" marks a freight or handling line that carries money but no stock. It is
                // exposed rather than filtered out here: dropping it would make the source disagree
                // with the order total, and a demand query can exclude it with line_type IS_NULL.
                ["line_type"] = Text(line.LineType),
                ["product_id"] = Id(line.Product?.Id),
                ["product_code"] = Text(line.Product?.ProductCode),
                ["product_description"] = Text(line.Product?.ProductDescription),
                ["order_quantity"] = line.OrderQuantity,
                ["unit_price"] = line.UnitPrice,
                ["discount_rate"] = line.DiscountRate,
                ["discounted_unit_price"] = line.DiscountedUnitPrice,
                ["line_total"] = line.LineTotal,
                ["line_tax"] = line.LineTax,
                ["line_tax_code"] = Text(line.LineTaxCode),
                ["unit_cost"] = line.UnitCost,
                ["volume"] = line.Volume,
                ["weight"] = line.Weight,
                ["line_comments"] = Text(line.Comments)
            };
        }
    }

    // ----- Sales shipment lines -----

    public static IEnumerable<Dictionary<string, object?>> SalesShipmentLines(UnleashedSalesShipment shipment)
    {
        ArgumentNullException.ThrowIfNull(shipment);

        foreach (var line in shipment.SalesShipmentLines)
        {
            yield return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["shipment_id"] = Id(shipment.Id),
                ["shipment_number"] = Text(shipment.ShipmentNumber),
                ["shipment_status"] = Text(shipment.ShipmentStatus),
                ["dispatch_date"] = shipment.DispatchDate,
                ["order_id"] = Id(shipment.OrderGuid),
                ["order_number"] = Text(shipment.OrderNumber),
                ["tracking_number"] = Text(shipment.TrackingNumber),
                ["shipping_company"] = Text(shipment.ShippingCompany?.Name),
                ["customer_code"] = Text(shipment.Customer?.CustomerCode),
                ["customer_name"] = Text(shipment.Customer?.CustomerName),
                ["customer_ref"] = Text(shipment.CustomerRef),
                ["warehouse_code"] = Text(shipment.Warehouse?.WarehouseCode),
                ["warehouse_name"] = Text(shipment.Warehouse?.WarehouseName),
                ["delivery_city"] = Text(shipment.DeliveryCity),
                ["delivery_region"] = Text(shipment.DeliveryRegion),
                ["delivery_country"] = Text(shipment.DeliveryCountry),
                ["delivery_post_code"] = Text(shipment.DeliveryPostCode),
                ["number_of_packages"] = shipment.NumberOfPackages,
                // Free text upstream ("100kg"), so it stays text. Parsing it into a number here would
                // mean inventing the unit for every value that omits one.
                ["shipment_weight"] = Text(shipment.ShipmentWeight),
                ["incoterm"] = Text(shipment.Incoterm),
                ["shipment_comments"] = Text(shipment.Comments),
                ["created_on"] = shipment.CreatedOn,
                ["created_by"] = Text(shipment.CreatedBy),
                ["last_modified_on"] = shipment.LastModifiedOn,
                ["last_modified_by"] = Text(shipment.LastModifiedBy),
                ["line_id"] = Id(line.Id),
                ["line_number"] = line.LineNumber,
                ["product_id"] = Id(line.Product?.Id),
                ["product_code"] = Text(line.Product?.ProductCode),
                ["product_description"] = Text(line.Product?.ProductDescription),
                ["shipment_qty"] = line.ShipmentQty,
                ["unit_cost"] = line.UnitCost,
                ["unit_cost_at_time_of_dispatch"] = line.UnitCostAtTimeOfDispatch,
                ["commercial_monetary_value"] = line.CommercialMonetaryValue,
                ["sales_order_line_number"] = line.SalesOrderLineNumber
            };
        }
    }

    // ----- Warehouse stock transfer lines -----

    public static IEnumerable<Dictionary<string, object?>> StockTransferLines(UnleashedStockTransfer transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);

        foreach (var line in transfer.TransferDetails)
        {
            yield return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["transfer_id"] = Id(transfer.Id),
                ["transfer_order_number"] = Text(transfer.TransferOrderNumber),
                ["order_date"] = transfer.OrderDate,
                ["delivery_date"] = transfer.DeliveryDate,
                ["transfer_status"] = Text(transfer.TransferStatus),
                ["source_warehouse_code"] = Text(transfer.SourceWarehouse?.WarehouseCode),
                ["source_warehouse_name"] = Text(transfer.SourceWarehouse?.WarehouseName),
                ["destination_warehouse_code"] = Text(transfer.DestinationWarehouse?.WarehouseCode),
                ["destination_warehouse_name"] = Text(transfer.DestinationWarehouse?.WarehouseName),
                ["transfer_comments"] = Text(transfer.Comments),
                ["created_on"] = transfer.CreatedOn,
                ["created_by"] = Text(transfer.CreatedBy),
                ["last_modified_on"] = transfer.LastModifiedOn,
                ["last_modified_by"] = Text(transfer.LastModifiedBy),
                ["line_id"] = Id(line.Id),
                ["line_number"] = line.LineNumber,
                // The line carries the product both as a reference and as a bare code
                // (WarehouseProductCode); the reference is preferred and the bare code is the
                // fallback, because a Parked transfer has been observed to carry only one of them.
                ["product_id"] = Id(line.Product?.Id),
                ["product_code"] = Text(line.Product?.ProductCode) ?? Text(line.WarehouseProductCode),
                ["transfer_quantity"] = line.TransferQuantity,
                ["available_quantity"] = line.AvailableQuantity,
                ["source_stock_on_hand_before_transfer"] = line.SourceWarehouseStockOnHandBeforeTransfer,
                ["average_landed_price_at_time_of_transfer"] = line.AverageLandedPriceAtTimeOfTransfer,
                ["receipt_fifo_date"] = line.ReceiptFIFODate,
                ["line_comments"] = Text(line.Comments)
            };
        }
    }

    // ----- Stock count lines -----

    public static IEnumerable<Dictionary<string, object?>> StockCountLines(UnleashedStockCount count)
    {
        ArgumentNullException.ThrowIfNull(count);

        foreach (var line in count.StockCountLines)
        {
            yield return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["stock_count_id"] = Id(count.Id),
                ["stock_take_code"] = Text(count.StockTakeCode),
                ["stock_take_name"] = Text(count.StockTakeName),
                ["description"] = Text(count.Description),
                ["count_type"] = Text(count.CountType),
                ["status"] = Text(count.Status),
                ["count_date"] = count.CountDate,
                ["completed_date"] = count.CompletedDate,
                ["warehouse_code"] = Text(count.Warehouse?.WarehouseCode),
                ["warehouse_name"] = Text(count.Warehouse?.WarehouseName),
                ["last_modified_on"] = count.LastModifiedOn,
                ["line_id"] = Id(line.Id),
                ["line_number"] = line.LineNumber,
                ["product_id"] = Id(line.Product?.Id),
                ["product_code"] = Text(line.Product?.ProductCode),
                ["product_description"] = Text(line.Product?.ProductDescription),
                ["on_hand_quantity"] = line.OnHandQuantity,
                ["available_quantity"] = line.AvailableQuantity,
                // Null means "not counted yet", not zero — the opposite of the purchase order's
                // receipt quantity, and the reason there is no derived variance column here. The
                // DSL's own `count_quantity - on_hand_quantity` yields null for an uncounted line,
                // which is the correct answer: a line nobody has walked past has no variance.
                ["count_quantity"] = line.CountQuantity,
                ["average_land_price_at_time_of_stock_take"] = line.AverageLandPriceAtTimeOfStockTake,
                ["expiry_date"] = line.ExpiryDate,
                ["receipt_fifo_date"] = line.ReceiptFIFODate
            };
        }
    }

    // ----- Supplier return lines -----

    public static IEnumerable<Dictionary<string, object?>> SupplierReturnLines(UnleashedSupplierReturn supplierReturn)
    {
        ArgumentNullException.ThrowIfNull(supplierReturn);

        foreach (var line in supplierReturn.SupplierReturnLines)
        {
            yield return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["supplier_return_id"] = Id(supplierReturn.Id),
                ["supplier_return_number"] = Text(supplierReturn.SupplierReturnNumber),
                ["status"] = Text(supplierReturn.Status),
                ["return_date"] = supplierReturn.ReturnDate,
                ["supplier_code"] = Text(supplierReturn.Supplier?.SupplierCode),
                ["supplier_name"] = Text(supplierReturn.Supplier?.SupplierName),
                ["supplier_ref"] = Text(supplierReturn.SupplierRef),
                ["purchase_order_number"] = Text(supplierReturn.PurchaseOrder?.OrderNumber),
                ["warehouse_code"] = Text(supplierReturn.Warehouse?.WarehouseCode),
                ["warehouse_name"] = Text(supplierReturn.Warehouse?.WarehouseName),
                ["currency_code"] = Text(supplierReturn.Currency?.CurrencyCode),
                ["exchange_rate"] = supplierReturn.ExchangeRate,
                ["return_comments"] = Text(supplierReturn.Comments),
                ["created_on"] = supplierReturn.CreatedOn,
                ["created_by"] = Text(supplierReturn.CreatedBy),
                ["last_modified_on"] = supplierReturn.LastModifiedOn,
                ["last_modified_by"] = Text(supplierReturn.LastModifiedBy),
                ["line_id"] = Id(line.Id),
                ["line_number"] = line.LineNumber,
                ["product_id"] = Id(line.Product?.Id),
                ["product_code"] = Text(line.Product?.ProductCode),
                ["product_description"] = Text(line.Product?.ProductDescription),
                ["order_quantity"] = line.OrderQuantity,
                ["order_price"] = line.OrderPrice,
                ["return_quantity"] = line.ReturnQuantity,
                ["return_price"] = line.ReturnPrice,
                ["return_cost"] = line.ReturnCost,
                ["purchase_unit_order_quantity"] = line.PurchaseUnitOrderQuantity,
                ["purchase_unit_return_quantity"] = line.PurchaseUnitReturnQuantity,
                ["return_reason"] = Text(line.ReturnReason),
                ["return_to_supplier"] = line.ReturnToSupplier,
                ["tax_rate"] = line.TaxRate,
                ["line_tax"] = line.LineTax,
                ["average_landed_price_before_return"] = line.AverageLandedPriceBeforeReturn,
                ["average_landed_price_after_return"] = line.AverageLandedPriceAfterReturn,
                ["stock_on_hand_before_return"] = line.StockOnHandBeforeReturn,
                ["required_date"] = line.RequiredDate,
                ["line_comments"] = Text(line.Comments)
            };
        }
    }

    private static string? Text(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? Id(Guid? value)
    {
        return value is null || value.Value == Guid.Empty ? null : value.Value.ToString();
    }
}
