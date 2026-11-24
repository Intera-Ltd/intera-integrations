using Intera.Unleashed.Api.Infrastructure.Json;
using System.Text.Json.Serialization;

namespace Intera.Unleashed.Api.Models;

// Only the fields this API exposes are declared. Unleashed's objects are much wider — a Product alone
// carries ten sell-price tiers, the Xero account mapping, image URLs and the customs classification —
// and a property nobody reads is a property that still has to be kept true.
//
// Every DateTime here goes through UnleashedDateTimeConverter: Unleashed writes dates in the WCF form
// "/Date(1459218740803)/", which System.Text.Json cannot read on its own.

/// <summary>One page of an Unleashed list response.</summary>
public sealed class UnleashedPage<T>
{
    /// <summary>
    /// Absent on the few endpoints Unleashed does not paginate. The client treats a missing block as
    /// "read on while a page comes back full" rather than as "one page only".
    /// </summary>
    public UnleashedPagination? Pagination { get; init; }

    public List<T> Items { get; init; } = [];
}

public sealed class UnleashedPagination
{
    public int NumberOfItems { get; init; }

    public int PageSize { get; init; }

    public int PageNumber { get; init; }

    public int NumberOfPages { get; init; }
}

// ----- Shared nested references -----
//
// Unleashed embeds a reference object wherever another entity is named. They are declared once here
// rather than per document: the same Warehouse shape appears on a purchase order, a stock count and a
// transfer, and three copies of it would drift.

public sealed class UnleashedProductRef
{
    [JsonPropertyName("Guid")]
    public Guid? Id { get; init; }

    public string? ProductCode { get; init; }

    public string? ProductDescription { get; init; }

    public UnleashedGroupRef? ProductGroup { get; init; }
}

public sealed class UnleashedWarehouseRef
{
    [JsonPropertyName("Guid")]
    public Guid? Id { get; init; }

    public string? WarehouseCode { get; init; }

    public string? WarehouseName { get; init; }
}

public sealed class UnleashedCustomerRef
{
    [JsonPropertyName("Guid")]
    public Guid? Id { get; init; }

    public string? CustomerCode { get; init; }

    public string? CustomerName { get; init; }
}

public sealed class UnleashedSupplierRef
{
    [JsonPropertyName("Guid")]
    public Guid? Id { get; init; }

    public string? SupplierCode { get; init; }

    public string? SupplierName { get; init; }
}

public sealed class UnleashedCurrencyRef
{
    [JsonPropertyName("Guid")]
    public Guid? Id { get; init; }

    public string? CurrencyCode { get; init; }
}

public sealed class UnleashedGroupRef
{
    [JsonPropertyName("Guid")]
    public Guid? Id { get; init; }

    public string? GroupName { get; init; }
}

public sealed class UnleashedBrandRef
{
    [JsonPropertyName("Guid")]
    public Guid? Id { get; init; }

    public string? BrandName { get; init; }
}

public sealed class UnleashedUnitOfMeasureRef
{
    [JsonPropertyName("Guid")]
    public Guid? Id { get; init; }

    public string? Name { get; init; }
}

public sealed class UnleashedShippingCompanyRef
{
    [JsonPropertyName("Guid")]
    public Guid? Id { get; init; }

    public string? Name { get; init; }
}

public sealed class UnleashedSalespersonRef
{
    [JsonPropertyName("Guid")]
    public Guid? Id { get; init; }

    public string? FullName { get; init; }

    public string? Email { get; init; }
}

public sealed class UnleashedPurchaseOrderRef
{
    [JsonPropertyName("Guid")]
    public Guid? Id { get; init; }

    public string? OrderNumber { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? OrderDate { get; init; }
}

// ----- Warehouses -----

public sealed class UnleashedWarehouse
{
    [JsonPropertyName("Guid")]
    public Guid? Id { get; init; }

    public string? WarehouseCode { get; init; }

    public string? WarehouseName { get; init; }

    public bool? IsDefault { get; init; }

    public bool? Obsolete { get; init; }

    public string? StreetNo { get; init; }

    public string? AddressLine1 { get; init; }

    public string? AddressLine2 { get; init; }

    public string? City { get; init; }

    public string? Region { get; init; }

    public string? Country { get; init; }

    public string? PostCode { get; init; }

    public string? PhoneNumber { get; init; }

    public string? FaxNumber { get; init; }

    public string? MobileNumber { get; init; }

    public string? DDINumber { get; init; }

    public string? ContactName { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? LastModifiedOn { get; init; }
}

// ----- Products -----

public sealed class UnleashedProduct
{
    [JsonPropertyName("Guid")]
    public Guid? Id { get; init; }

    public string? ProductCode { get; init; }

    public string? ProductDescription { get; init; }

    public string? Barcode { get; init; }

    public UnleashedGroupRef? ProductGroup { get; init; }

    public UnleashedGroupRef? ProductSubGroup { get; init; }

    public UnleashedBrandRef? ProductBrand { get; init; }

    public UnleashedUnitOfMeasureRef? UnitOfMeasure { get; init; }

    public UnleashedUnitOfMeasureRef? DefaultPurchasesUnitOfMeasure { get; init; }

    public UnleashedSupplierRef? Supplier { get; init; }

    public decimal? PackSize { get; init; }

    public decimal? Width { get; init; }

    public decimal? Height { get; init; }

    public decimal? Depth { get; init; }

    public decimal? Weight { get; init; }

    public decimal? MinStockAlertLevel { get; init; }

    public decimal? MaxStockAlertLevel { get; init; }

    public decimal? LastCost { get; init; }

    public decimal? DefaultPurchasePrice { get; init; }

    public decimal? DefaultSellPrice { get; init; }

    public decimal? AverageLandPrice { get; init; }

    public decimal? NominalCost { get; init; }

    public decimal? MinimumSellPrice { get; init; }

    public decimal? MinimumSaleQuantity { get; init; }

    public decimal? MinimumOrderQuantity { get; init; }

    public bool? Obsolete { get; init; }

    public bool? NeverDiminishing { get; init; }

    public bool? IsComponent { get; init; }

    public bool? IsAssembledProduct { get; init; }

    public bool? IsSerialized { get; init; }

    public bool? IsBatchTracked { get; init; }

    public bool? IsPurchasable { get; init; }

    public bool? IsSellable { get; init; }

    public string? BinLocation { get; init; }

    public string? Notes { get; init; }

    public string? CreatedBy { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? CreatedOn { get; init; }

    public string? LastModifiedBy { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? LastModifiedOn { get; init; }
}

// ----- Stock on hand -----

public sealed class UnleashedStockOnHand
{
    [JsonPropertyName("Guid")]
    public Guid? Id { get; init; }

    public Guid? ProductGuid { get; init; }

    public string? ProductCode { get; init; }

    public string? ProductDescription { get; init; }

    public string? ProductGroupName { get; init; }

    public Guid? WarehouseId { get; init; }

    public string? WarehouseCode { get; init; }

    /// <summary>The warehouse <b>name</b> — a bare string here, not the reference object other endpoints use.</summary>
    public string? Warehouse { get; init; }

    public decimal? QtyOnHand { get; init; }

    public decimal? AllocatedQty { get; init; }

    public decimal? AvailableQty { get; init; }

    public decimal? OnPurchase { get; init; }

    public decimal? AvgCost { get; init; }

    public decimal? TotalCost { get; init; }

    public int? DaysSinceLastSale { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? LastModifiedOn { get; init; }
}

// ----- Purchase orders -----

public sealed class UnleashedPurchaseOrder
{
    [JsonPropertyName("Guid")]
    public Guid? Id { get; init; }

    public string? OrderNumber { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? OrderDate { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? CompletedDate { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? ReceivedDate { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? SupplierInvoiceDate { get; init; }

    public string? OrderStatus { get; init; }

    public string? CustomOrderStatus { get; init; }

    public UnleashedSupplierRef? Supplier { get; init; }

    public string? SupplierRef { get; init; }

    public UnleashedWarehouseRef? Warehouse { get; init; }

    public UnleashedCurrencyRef? Currency { get; init; }

    public decimal? ExchangeRate { get; init; }

    public string? Comments { get; init; }

    public string? CreatedBy { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? CreatedOn { get; init; }

    public string? LastModifiedBy { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? LastModifiedOn { get; init; }

    public List<UnleashedPurchaseOrderLine> PurchaseOrderLines { get; init; } = [];
}

public sealed class UnleashedPurchaseOrderLine
{
    [JsonPropertyName("Guid")]
    public Guid? Id { get; init; }

    public int? LineNumber { get; init; }

    public UnleashedProductRef? Product { get; init; }

    public UnleashedUnitOfMeasureRef? UnitOfMeasure { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? DeliveryDate { get; init; }

    public decimal? OrderQuantity { get; init; }

    public decimal? ReceiptQuantity { get; init; }

    public decimal? PurchaseUnitOrderQuantity { get; init; }

    public decimal? PurchaseUnitReceiptQuantity { get; init; }

    public decimal? UnitPrice { get; init; }

    public decimal? DiscountedUnitPrice { get; init; }

    public decimal? DiscountRate { get; init; }

    public decimal? LineTotal { get; init; }

    public decimal? LineTax { get; init; }

    public string? LineTaxCode { get; init; }

    public decimal? LineTaxRate { get; init; }

    public decimal? Volume { get; init; }

    public decimal? Weight { get; init; }

    public string? Comments { get; init; }
}

// ----- Sales orders -----

public sealed class UnleashedSalesOrder
{
    [JsonPropertyName("Guid")]
    public Guid? Id { get; init; }

    public string? OrderNumber { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? OrderDate { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? RequiredDate { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? CompletedDate { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? ReceivedDate { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? PaymentDueDate { get; init; }

    public string? OrderStatus { get; init; }

    public string? CustomOrderStatus { get; init; }

    public UnleashedCustomerRef? Customer { get; init; }

    public string? CustomerRef { get; init; }

    public UnleashedWarehouseRef? Warehouse { get; init; }

    public UnleashedCurrencyRef? Currency { get; init; }

    public decimal? ExchangeRate { get; init; }

    public UnleashedSalespersonRef? Salesperson { get; init; }

    public string? SalesOrderGroup { get; init; }

    public string? DeliveryMethod { get; init; }

    public string? DeliveryCity { get; init; }

    public string? DeliveryRegion { get; init; }

    public string? DeliveryCountry { get; init; }

    public string? DeliveryPostCode { get; init; }

    public string? Comments { get; init; }

    public string? CreatedBy { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? CreatedOn { get; init; }

    public string? LastModifiedBy { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? LastModifiedOn { get; init; }

    public List<UnleashedSalesOrderLine> SalesOrderLines { get; init; } = [];
}

public sealed class UnleashedSalesOrderLine
{
    [JsonPropertyName("Guid")]
    public Guid? Id { get; init; }

    public int? LineNumber { get; init; }

    public string? LineType { get; init; }

    public UnleashedProductRef? Product { get; init; }

    public decimal? OrderQuantity { get; init; }

    public decimal? UnitPrice { get; init; }

    public decimal? DiscountRate { get; init; }

    public decimal? DiscountedUnitPrice { get; init; }

    public decimal? LineTotal { get; init; }

    public decimal? LineTax { get; init; }

    public string? LineTaxCode { get; init; }

    public decimal? UnitCost { get; init; }

    public decimal? Volume { get; init; }

    public decimal? Weight { get; init; }

    public string? Comments { get; init; }
}

// ----- Sales shipments -----

public sealed class UnleashedSalesShipment
{
    [JsonPropertyName("Guid")]
    public Guid? Id { get; init; }

    public string? ShipmentNumber { get; init; }

    public string? ShipmentStatus { get; init; }

    public string? OrderNumber { get; init; }

    public Guid? OrderGuid { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? DispatchDate { get; init; }

    public string? TrackingNumber { get; init; }

    public UnleashedShippingCompanyRef? ShippingCompany { get; init; }

    public UnleashedCustomerRef? Customer { get; init; }

    public string? CustomerRef { get; init; }

    public UnleashedWarehouseRef? Warehouse { get; init; }

    public string? DeliveryCity { get; init; }

    public string? DeliveryRegion { get; init; }

    public string? DeliveryCountry { get; init; }

    public string? DeliveryPostCode { get; init; }

    public int? NumberOfPackages { get; init; }

    /// <summary>A free-text string upstream ("100kg"), not a number — carried through as written.</summary>
    public string? ShipmentWeight { get; init; }

    public string? Incoterm { get; init; }

    public string? Comments { get; init; }

    public string? CreatedBy { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? CreatedOn { get; init; }

    public string? LastModifiedBy { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? LastModifiedOn { get; init; }

    public List<UnleashedSalesShipmentLine> SalesShipmentLines { get; init; } = [];
}

public sealed class UnleashedSalesShipmentLine
{
    [JsonPropertyName("Guid")]
    public Guid? Id { get; init; }

    public int? LineNumber { get; init; }

    public UnleashedProductRef? Product { get; init; }

    public decimal? ShipmentQty { get; init; }

    public decimal? UnitCost { get; init; }

    public decimal? UnitCostAtTimeOfDispatch { get; init; }

    public decimal? CommercialMonetaryValue { get; init; }

    public int? SalesOrderLineNumber { get; init; }
}

// ----- Warehouse stock transfers -----

public sealed class UnleashedStockTransfer
{
    [JsonPropertyName("Guid")]
    public Guid? Id { get; init; }

    public string? TransferOrderNumber { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? OrderDate { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? DeliveryDate { get; init; }

    public string? TransferStatus { get; init; }

    public UnleashedWarehouseRef? SourceWarehouse { get; init; }

    public UnleashedWarehouseRef? DestinationWarehouse { get; init; }

    public string? Comments { get; init; }

    public string? CreatedBy { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? CreatedOn { get; init; }

    public string? LastModifiedBy { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? LastModifiedOn { get; init; }

    public List<UnleashedStockTransferLine> TransferDetails { get; init; } = [];
}

public sealed class UnleashedStockTransferLine
{
    [JsonPropertyName("Guid")]
    public Guid? Id { get; init; }

    public int? LineNumber { get; init; }

    public UnleashedProductRef? Product { get; init; }

    public string? WarehouseProductCode { get; init; }

    public decimal? TransferQuantity { get; init; }

    public decimal? AvailableQuantity { get; init; }

    public decimal? SourceWarehouseStockOnHandBeforeTransfer { get; init; }

    public decimal? AverageLandedPriceAtTimeOfTransfer { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? ReceiptFIFODate { get; init; }

    public string? Comments { get; init; }
}

// ----- Stock counts -----

public sealed class UnleashedStockCount
{
    [JsonPropertyName("Guid")]
    public Guid? Id { get; init; }

    public string? StockTakeCode { get; init; }

    public string? StockTakeName { get; init; }

    public string? Description { get; init; }

    public string? CountType { get; init; }

    public string? Status { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? CountDate { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? CompletedDate { get; init; }

    public UnleashedWarehouseRef? Warehouse { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? LastModifiedOn { get; init; }

    public List<UnleashedStockCountLine> StockCountLines { get; init; } = [];
}

public sealed class UnleashedStockCountLine
{
    [JsonPropertyName("Guid")]
    public Guid? Id { get; init; }

    public int? LineNumber { get; init; }

    public UnleashedProductRef? Product { get; init; }

    public decimal? OnHandQuantity { get; init; }

    public decimal? AvailableQuantity { get; init; }

    /// <summary>Null until the line has actually been counted — see <c>UnleashedRows</c>.</summary>
    public decimal? CountQuantity { get; init; }

    public decimal? AverageLandPriceAtTimeOfStockTake { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? ExpiryDate { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? ReceiptFIFODate { get; init; }
}

// ----- Supplier returns -----

public sealed class UnleashedSupplierReturn
{
    [JsonPropertyName("Guid")]
    public Guid? Id { get; init; }

    public string? SupplierReturnNumber { get; init; }

    public string? Status { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? ReturnDate { get; init; }

    public UnleashedSupplierRef? Supplier { get; init; }

    public string? SupplierRef { get; init; }

    public UnleashedPurchaseOrderRef? PurchaseOrder { get; init; }

    public UnleashedWarehouseRef? Warehouse { get; init; }

    public UnleashedCurrencyRef? Currency { get; init; }

    public decimal? ExchangeRate { get; init; }

    public string? Comments { get; init; }

    public string? CreatedBy { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? CreatedOn { get; init; }

    public string? LastModifiedBy { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? LastModifiedOn { get; init; }

    public List<UnleashedSupplierReturnLine> SupplierReturnLines { get; init; } = [];
}

public sealed class UnleashedSupplierReturnLine
{
    [JsonPropertyName("Guid")]
    public Guid? Id { get; init; }

    public int? LineNumber { get; init; }

    public UnleashedProductRef? Product { get; init; }

    public decimal? OrderQuantity { get; init; }

    public decimal? OrderPrice { get; init; }

    public decimal? ReturnQuantity { get; init; }

    public decimal? ReturnPrice { get; init; }

    public decimal? ReturnCost { get; init; }

    public decimal? PurchaseUnitOrderQuantity { get; init; }

    public decimal? PurchaseUnitReturnQuantity { get; init; }

    public string? ReturnReason { get; init; }

    public bool? ReturnToSupplier { get; init; }

    public decimal? TaxRate { get; init; }

    public decimal? LineTax { get; init; }

    public decimal? AverageLandedPriceBeforeReturn { get; init; }

    public decimal? AverageLandedPriceAfterReturn { get; init; }

    public decimal? StockOnHandBeforeReturn { get; init; }

    [JsonConverter(typeof(UnleashedDateTimeConverter))]
    public DateTime? RequiredDate { get; init; }

    public string? Comments { get; init; }
}
