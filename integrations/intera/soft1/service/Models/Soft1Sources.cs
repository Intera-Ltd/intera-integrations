using Intera.Common.Infrastructure.Query;
using System.Text.Json.Serialization;

namespace Intera.Soft1.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter<Soft1FieldType>))]
public enum Soft1FieldType
{
    Text,
    Number,
    Date,
    Flag
}

/// <param name="Lookup">Source id whose rows name this id, for the plugin's field lookup.</param>
/// <param name="LookupValue">Field of <paramref name="Lookup"/> holding the id.</param>
/// <param name="LookupLabel">Field of <paramref name="Lookup"/> holding the label.</param>
public sealed record Soft1Field(
    string Name,
    Soft1FieldType Type,
    string Label,
    string? Lookup = null,
    string? LookupValue = null,
    string? LookupLabel = null);

/// <param name="DateField">
/// The field the reporting window is read from: its bounds in the filter narrow what is read from Soft1.
/// </param>
public sealed record Soft1Source(string Id, string Name, string Description, string? DateField, IReadOnlyList<Soft1Field> Fields)
{
    [JsonIgnore]
    public IReadOnlyDictionary<string, InMemoryQueryField> Allowlist { get; } =
        InMemoryQueryFieldsBuilder.Build([.. Fields.Select(static field => field.Name)]);
}

/// <summary>
/// Every data source this API serves: its id, its fields and their types. The one list the allowlists, the
/// row projection and the plugin's field descriptors (<c>GET /api/Schema/sources</c>) are all built from, so
/// the three cannot drift.
/// </summary>
public static class Soft1Sources
{
    public const string Customers = "customers";
    public const string Suppliers = "suppliers";
    public const string Items = "items";
    public const string SalesDocuments = "sales_documents";
    public const string SalesDocumentLines = "sales_document_lines";
    public const string PurchaseDocuments = "purchase_documents";
    public const string PurchaseDocumentLines = "purchase_document_lines";
    public const string CustomerTransactions = "customer_transactions";
    public const string SupplierTransactions = "supplier_transactions";
    public const string CustomerBalances = "customer_balances";
    public const string SupplierBalances = "supplier_balances";
    public const string OpenReceivables = "open_receivables";
    public const string OpenPayables = "open_payables";
    public const string ReceivablesAgeing = "receivables_ageing";
    public const string PayablesAgeing = "payables_ageing";
    public const string ChartOfAccounts = "chart_of_accounts";
    public const string GeneralLedgerEntries = "general_ledger_entries";
    public const string AccountBalances = "account_balances";
    public const string CashBankAccounts = "cash_bank_accounts";
    public const string CashBankTransactions = "cash_bank_transactions";
    public const string VatTransactions = "vat_transactions";
    public const string StockBalances = "stock_balances";
    public const string StockMovements = "stock_movements";
    public const string Warehouses = "warehouses";
    public const string SalesOrders = "sales_orders";
    public const string SalesOrderLines = "sales_order_lines";
    public const string PurchaseOrders = "purchase_orders";
    public const string PurchaseOrderLines = "purchase_order_lines";
    public const string GoodsReceipts = "goods_receipts";
    public const string GoodsReceiptLines = "goods_receipt_lines";
    public const string Shipments = "shipments";
    public const string ShipmentLines = "shipment_lines";
    public const string ItemSupplierPrices = "item_supplier_prices";
    public const string SalesByCustomer = "sales_by_customer";
    public const string SalesByItem = "sales_by_item";
    public const string SalesBySalesperson = "sales_by_salesperson";
    public const string CustomerCreditStatus = "customer_credit_status";
    public const string Projects = "projects";
    public const string ProjectTransactions = "project_transactions";
    public const string ProjectFinancials = "project_financials";
    public const string Salespeople = "salespeople";
    public const string PaymentTerms = "payment_terms";
    public const string DocumentTypes = "document_types";
    public const string Currencies = "currencies";
    public const string Branches = "branches";
    public const string Companies = "companies";
    public const string ItemCategories = "item_categories";
    public const string CustomerCategories = "customer_categories";
    public const string SupplierCategories = "supplier_categories";
    public const string TaxCodes = "tax_codes";
    public const string Units = "units";

    private static Soft1Field T(string name, string label) => new(name, Soft1FieldType.Text, label);

    private static Soft1Field N(string name, string label) => new(name, Soft1FieldType.Number, label);

    private static Soft1Field D(string name, string label) => new(name, Soft1FieldType.Date, label);

    private static Soft1Field F(string name, string label) => new(name, Soft1FieldType.Flag, label);

    private static Soft1Field Customer(string name = "customer_id", string label = "Customer ID") =>
        new(name, Soft1FieldType.Text, label, Customers, "customer_id", "name");

    private static Soft1Field Supplier(string name = "supplier_id", string label = "Supplier ID") =>
        new(name, Soft1FieldType.Text, label, Suppliers, "supplier_id", "name");

    private static Soft1Field Item() => new("item_id", Soft1FieldType.Text, "Item ID", Items, "item_id", "description");

    private static Soft1Field Warehouse() => new("warehouse_id", Soft1FieldType.Text, "Warehouse ID", Warehouses, "warehouse_id", "name");

    private static Soft1Field Salesperson(string name = "salesperson_id", string label = "Salesperson ID") =>
        new(name, Soft1FieldType.Text, label, Salespeople, "salesperson_id", "name");

    private static Soft1Field Project() => new("project_id", Soft1FieldType.Text, "Project ID", Projects, "project_id", "name");

    private static Soft1Field Account(string name = "account_id", string label = "Account ID") =>
        new(name, Soft1FieldType.Text, label, ChartOfAccounts, "account_id", "account_name");

    private static Soft1Field Branch() => new("branch_id", Soft1FieldType.Text, "Branch ID", Branches, "branch_id", "name");

    private static Soft1Field CustomerCategory() =>
        new("category_id", Soft1FieldType.Text, "Category ID", CustomerCategories, "category_id", "name");

    private static Soft1Field SupplierCategory() =>
        new("category_id", Soft1FieldType.Text, "Category ID", SupplierCategories, "category_id", "name");

    private static Soft1Field ItemCategory() =>
        new("category_id", Soft1FieldType.Text, "Category ID", ItemCategories, "category_id", "name");

    private static Soft1Field[] DocumentHeader(bool sales) =>
    [
        T("document_id", "Document ID"),
        T("document_number", "Document number"),
        T("document_type", "Document type"),
        T("document_type_id", "Document type ID"),
        T("series", "Series"),
        T("document_category", "Document category"),
        sales ? Customer() : Supplier(),
        T(sales ? "customer_code" : "supplier_code", sales ? "Customer code" : "Supplier code"),
        T(sales ? "customer_name" : "supplier_name", sales ? "Customer" : "Supplier"),
        D("document_date", "Document date")
    ];

    private static Soft1Field[] Trader(bool customers) =>
    [
        customers ? Customer() : Supplier(),
        T(customers ? "customer_code" : "supplier_code", "Code"),
        T("name", "Name"),
        T("vat_number", "VAT number"),
        T("status", "Status"),
        F("active", "Active"),
        T("category", "Category"),
        customers ? CustomerCategory() : SupplierCategory(),
        .. customers ? (Soft1Field[])[Salesperson(), T("salesperson_name", "Salesperson")] : [],
        T("currency", "Currency"),
        T("payment_terms", "Payment terms"),
        T("payment_terms_id", "Payment terms ID"),
        .. customers ? (Soft1Field[])[N("credit_limit", "Credit limit")] : [],
        N("balance", "Balance"),
        T("country", "Country"),
        T("city", "City"),
        T("address", "Address"),
        T("email", "Email"),
        T("phone", "Phone"),
        D("created_at", "Created"),
        D("updated_at", "Updated")
    ];

    private static Soft1Field[] Transaction(bool customers) =>
    [
        T("transaction_id", "Transaction ID"),
        customers ? Customer() : Supplier(),
        D("transaction_date", "Transaction date"),
        T("transaction_type", "Transaction type"),
        T("transaction_type_id", "Transaction type ID"),
        T("document_id", "Document ID"),
        T("document_number", "Document number"),
        N("debit", "Debit"),
        N("credit", "Credit"),
        N("balance_effect", "Balance effect"),
        N("amount_local", "Amount in company currency"),
        T("currency", "Currency"),
        T("reference", "Reference"),
        D("due_date", "Due date"),
        Project(),
        .. customers ? (Soft1Field[])[Salesperson()] : [],
        Branch()
    ];

    private static Soft1Field[] Balance(bool customers) =>
    [
        customers ? Customer() : Supplier(),
        T("name", "Name"),
        T("currency", "Currency"),
        N("opening_balance", "Opening balance"),
        N("debit", "Debit"),
        N("credit", "Credit"),
        N("outstanding_balance", "Outstanding balance"),
        N("overdue_balance", "Overdue balance"),
        D("last_transaction_date", "Last transaction")
    ];

    private static Soft1Field[] OpenItem(bool customers) =>
    [
        T("open_item_id", "Open item ID"),
        T("document_id", "Document ID"),
        customers ? Customer() : Supplier(),
        T("invoice_number", "Invoice number"),
        D("invoice_date", "Invoice date"),
        D("due_date", "Due date"),
        N("original_amount", "Original amount"),
        N("paid_amount", "Paid amount"),
        N("outstanding_amount", "Outstanding amount"),
        T("currency", "Currency"),
        N("days_overdue", "Days overdue"),
        .. customers ? (Soft1Field[])[Salesperson()] : [],
        Project()
    ];

    private static Soft1Field[] Ageing(bool customers) =>
    [
        customers ? Customer() : Supplier(),
        T("name", "Name"),
        T("currency", "Currency"),
        N("current_amount", "Not yet due"),
        N("days_1_30", "1-30 days"),
        N("days_31_60", "31-60 days"),
        N("days_61_90", "61-90 days"),
        N("days_91_120", "91-120 days"),
        N("days_120_plus", "Over 120 days"),
        N("total_outstanding", "Total outstanding")
    ];

    private static Soft1Field[] Lines(bool sales, bool orders) =>
    [
        T("line_id", "Line ID"),
        T(orders ? "order_id" : "document_id", orders ? "Order ID" : "Document ID"),
        T(orders ? "order_number" : "document_number", orders ? "Order number" : "Document number"),
        D(orders ? "order_date" : "document_date", orders ? "Order date" : "Document date"),
        sales ? Customer() : Supplier(),
        N("line_number", "Line number"),
        Item(),
        T("item_code", "Item code"),
        T("description", "Description"),
        .. orders
            ? (Soft1Field[])
            [
                N("ordered_qty", "Ordered quantity"),
                N(sales ? "delivered_qty" : "received_qty", sales ? "Delivered quantity" : "Received quantity"),
                N("invoiced_qty", "Invoiced quantity"),
                N("cancelled_qty", "Cancelled quantity"),
                N("open_qty", "Open quantity")
            ]
            : [N("quantity", "Quantity")],
        T("unit", "Unit"),
        N("unit_price", "Unit price"),
        N("discount", "Discount, %"),
        N("discount_amount", "Discount amount"),
        N("net_amount", "Net amount"),
        N("vat_amount", "VAT amount"),
        N(orders ? "line_total" : "total_amount", orders ? "Line total" : "Total amount"),
        T("currency", "Currency"),
        Warehouse(),
        Project(),
        D(orders ? (sales ? "requested_date" : "expected_date") : "delivery_date",
            orders ? (sales ? "Requested date" : "Expected date") : "Delivery date")
    ];

    private static Soft1Field[] SimpleReference(string idName, string idLabel) =>
    [
        T(idName, idLabel),
        T("code", "Code"),
        T("name", "Name"),
        F("active", "Active")
    ];

    public static readonly IReadOnlyList<Soft1Source> All =
    [
        new(Customers, "Customers",
            "Customers (`CUSTOMER`), one row per customer, with the current balance where the browser shows it.",
            null, Trader(customers: true)),

        new(Suppliers, "Suppliers", "Suppliers (`SUPPLIER`), one row per supplier.", null, Trader(customers: false)),

        new(Items, "Items",
            "Items (`ITEM`): codes, classification, prices and — where the browser shows it — the stock quantity.",
            null,
            [
                Item(), T("item_code", "Item code"), T("description", "Description"), T("category", "Category"), ItemCategory(),
                T("group", "Group"), T("group_id", "Group ID"), T("brand", "Brand"), T("brand_id", "Brand ID"),
                T("unit", "Unit"), T("unit_id", "Unit ID"), T("vat_code", "VAT code"), T("vat_id", "VAT ID"),
                T("status", "Status"), F("active", "Active"), N("sales_price", "Sales (wholesale) price"),
                N("retail_price", "Retail price"), N("cost_price", "Last purchase price"), T("currency", "Currency"),
                N("quantity_on_hand", "Quantity on hand"), D("created_at", "Created"), D("updated_at", "Updated")
            ]),

        new(SalesDocuments, "Sales documents",
            "Sales documents (`SALDOC`) except orders: invoices, credit notes, delivery notes. `document_category` tells them apart.",
            "document_date",
            [
                .. DocumentHeader(sales: true), D("due_date", "Due date"), T("status", "Status"), F("cancelled", "Cancelled"),
                Salesperson(), T("currency", "Currency"), N("net_amount", "Net amount"), N("vat_amount", "VAT amount"),
                N("total_amount", "Total amount"), Project(), T("reference", "Reference"), T("payment_terms", "Payment terms"),
                D("created_at", "Created"), D("updated_at", "Updated")
            ]),

        new(SalesDocumentLines, "Sales document lines",
            "Item lines of sales documents (not orders). Each document costs one Soft1 call, so narrow by date or customer.",
            "document_date", Lines(sales: true, orders: false)),

        new(PurchaseDocuments, "Purchase documents",
            "Purchase documents (`PURDOC`) except orders: invoices, credit notes, goods receipts.",
            "document_date",
            [
                .. DocumentHeader(sales: false), D("due_date", "Due date"), T("status", "Status"), F("cancelled", "Cancelled"),
                T("currency", "Currency"), N("net_amount", "Net amount"), N("vat_amount", "VAT amount"),
                N("total_amount", "Total amount"), Project(), T("reference", "Reference"), T("payment_terms", "Payment terms"),
                D("created_at", "Created"), D("updated_at", "Updated")
            ]),

        new(PurchaseDocumentLines, "Purchase document lines",
            "Item lines of purchase documents (not orders). Each document costs one Soft1 call.",
            "document_date", Lines(sales: false, orders: false)),

        new(CustomerTransactions, "Customer transactions",
            "The customer ledger (`TRDTRN`): every posting that moved a customer balance, debit or credit.",
            "transaction_date", Transaction(customers: true)),

        new(SupplierTransactions, "Supplier transactions",
            "The supplier ledger (`TRDTRN`): every posting that moved a supplier balance.",
            "transaction_date", Transaction(customers: false)),

        new(CustomerBalances, "Customer balances",
            "Per customer, in company currency: the balance now, this period's debit and credit, and the overdue part.",
            null, Balance(customers: true)),

        new(SupplierBalances, "Supplier balances", "Per supplier, in company currency: balance, debit, credit, overdue.",
            null, Balance(customers: false)),

        new(OpenReceivables, "Open receivables",
            "Unsettled customer instalments (`FINPAYTERMS`), one row per instalment, with days overdue.",
            null, OpenItem(customers: true)),

        new(OpenPayables, "Open payables", "Unsettled supplier instalments (`FINPAYTERMS`).", null, OpenItem(customers: false)),

        new(ReceivablesAgeing, "Receivables ageing", "Open receivables per customer and currency in overdue buckets.",
            null, Ageing(customers: true)),

        new(PayablesAgeing, "Payables ageing", "Open payables per supplier and currency in overdue buckets.",
            null, Ageing(customers: false)),

        new(ChartOfAccounts, "Chart of accounts", "General ledger accounts (`ACNT`).", null,
        [
            Account(), T("account_code", "Account code"), T("account_name", "Account name"), T("account_type", "Account type"),
            T("account_group", "Account group"), T("account_category", "Account category"), Account("parent_account_id", "Parent account ID"),
            T("currency", "Currency"), F("active", "Active")
        ]),

        new(GeneralLedgerEntries, "General ledger entries", "Journal lines (`ACNTRN`), one row per account line.",
            "posting_date",
        [
            T("entry_id", "Entry ID"), D("posting_date", "Posting date"), Account(), T("account_code", "Account code"),
            T("document_id", "Journal entry ID"), T("document_number", "Document number"), T("description", "Description"),
            N("debit", "Debit"), N("credit", "Credit"), N("debit_local", "Debit, company currency"),
            N("credit_local", "Credit, company currency"), T("currency", "Currency"), Customer(), Supplier(), Project(),
            Branch(), T("reference", "Reference"), F("cancelled", "Cancelled")
        ]),

        new(AccountBalances, "Account balances",
            "Per account and fiscal period (`ACNBALSHEET`), in company currency: opening, debit, credit, closing.",
            "period_start",
        [
            Account(), T("account_code", "Account code"), T("account_name", "Account name"), T("period", "Period"),
            N("fiscal_year", "Fiscal year"), N("period_number", "Period number"), D("period_start", "Period start"),
            T("currency", "Currency"), N("opening_balance", "Opening balance"), N("debit", "Debit"), N("credit", "Credit"),
            N("closing_balance", "Closing balance")
        ]),

        new(CashBankAccounts, "Cash and bank accounts", "Cash accounts (`TRDR`, entity 14) with their current balance.", null,
        [
            T("account_id", "Account ID"), T("account_code", "Account code"), T("account_name", "Account name"),
            T("bank_name", "Bank"), T("iban", "IBAN"), T("currency", "Currency"), T("account_type", "Account type"),
            N("current_balance", "Current balance"), N("available_balance", "Available balance"), F("active", "Active"),
            D("last_update", "Last update")
        ]),

        new(CashBankTransactions, "Cash and bank transactions",
            "Postings on cash accounts (`TRDTRN`, entity 14), with the customer or supplier of the same document.",
            "transaction_date",
        [
            T("transaction_id", "Transaction ID"),
            new("bank_account_id", Soft1FieldType.Text, "Cash/bank account ID", CashBankAccounts, "account_id", "account_name"),
            D("transaction_date", "Transaction date"), D("value_date", "Value date"), N("amount", "Amount"),
            N("amount_local", "Amount in company currency"), T("currency", "Currency"), T("transaction_type", "Transaction type"),
            T("counterparty", "Counterparty"), T("reference", "Reference"), T("document_number", "Document number"),
            Customer(), Supplier(), T("matched_document_id", "Document ID")
        ]),

        new(VatTransactions, "VAT transactions",
            "VAT analysis of sales and purchase invoices (`VATANAL`), one row per VAT rate per invoice. One Soft1 call per invoice.",
            "document_date",
        [
            T("transaction_id", "Transaction ID"), T("document_id", "Document ID"), T("document_number", "Document number"),
            D("document_date", "Document date"), T("direction", "Direction"), T("vat_code", "VAT code"), N("vat_rate", "VAT rate, %"),
            N("taxable_amount", "Taxable amount"), N("vat_amount", "VAT amount"), T("currency", "Currency"), Customer(), Supplier(),
            Account(), T("period", "Period")
        ]),

        new(StockBalances, "Stock balances",
            "Quantity and value per item and warehouse for the current fiscal year (`MTRFINDATA`).", null,
        [
            Item(), T("item_code", "Item code"), T("description", "Description"), Warehouse(), N("quantity_on_hand", "Quantity on hand"),
            N("quantity_reserved", "Quantity reserved"), N("quantity_available", "Quantity available"),
            N("quantity_on_order", "Quantity on order"), T("unit", "Unit"), N("average_cost", "Average cost"),
            N("stock_value", "Stock value"), D("last_movement_date", "Last movement"), N("fiscal_year", "Fiscal year")
        ]),

        new(StockMovements, "Stock movements", "Item movements of every document that moves stock (`MTRTRN`).", "movement_date",
        [
            T("movement_id", "Movement ID"), D("movement_date", "Movement date"), Item(), Warehouse(), T("movement_type", "Movement type"),
            T("movement_type_id", "Movement type ID"), N("quantity", "Quantity"), T("unit", "Unit"), N("unit_cost", "Unit price"),
            N("total_cost", "Cost value"), N("value", "Transaction value"), T("document_id", "Document ID"),
            T("document_number", "Document number"), T("module", "Module"), Customer(), Supplier(), Project()
        ]),

        new(Warehouses, "Warehouses", "Warehouses (`WHOUSE`).", null,
        [
            Warehouse(), T("warehouse_code", "Warehouse code"), T("name", "Name"), Branch(), T("location", "Location"),
            T("address", "Address"), F("active", "Active")
        ]),

        new(SalesOrders, "Sales orders",
            "Sales documents whose type is an order. `status` is open, partially or fully converted, or cancelled.",
            "order_date",
        [
            T("order_id", "Order ID"), T("order_number", "Order number"), T("document_type", "Document type"), T("series", "Series"),
            Customer(), T("customer_code", "Customer code"), T("customer_name", "Customer"), D("order_date", "Order date"),
            D("requested_date", "Requested date"), T("status", "Status"), F("cancelled", "Cancelled"), Salesperson(),
            T("currency", "Currency"), N("net_amount", "Net amount"), N("vat_amount", "VAT amount"), N("total_amount", "Total amount"),
            Project(), T("customer_reference", "Reference")
        ]),

        new(SalesOrderLines, "Sales order lines", "Item lines of sales orders with ordered, converted and open quantity.",
            "order_date", Lines(sales: true, orders: true)),

        new(PurchaseOrders, "Purchase orders", "Purchase documents whose type is an order.", "order_date",
        [
            T("order_id", "Order ID"), T("order_number", "Order number"), T("document_type", "Document type"), T("series", "Series"),
            Supplier(), T("supplier_code", "Supplier code"), T("supplier_name", "Supplier"), D("order_date", "Order date"),
            D("expected_date", "Expected date"), T("status", "Status"), F("cancelled", "Cancelled"), T("currency", "Currency"),
            N("net_amount", "Net amount"), N("vat_amount", "VAT amount"), N("total_amount", "Total amount"), Project(),
            T("supplier_reference", "Reference")
        ]),

        new(PurchaseOrderLines, "Purchase order lines", "Item lines of purchase orders with ordered, received and open quantity.",
            "order_date", Lines(sales: false, orders: true)),

        new(GoodsReceipts, "Goods receipts", "Purchase documents whose type is a delivery note (goods received).",
            "receipt_date",
        [
            T("receipt_id", "Receipt ID"), T("receipt_number", "Receipt number"), T("document_type", "Document type"),
            Supplier(), T("supplier_name", "Supplier"), D("receipt_date", "Receipt date"), T("purchase_order_id", "Purchase order ID"),
            Warehouse(), T("status", "Status"), F("cancelled", "Cancelled"), T("reference", "Reference"), Project(),
            N("total_amount", "Total amount"), T("currency", "Currency")
        ]),

        new(GoodsReceiptLines, "Goods receipt lines", "Item lines of goods receipts. One Soft1 call per receipt.", "receipt_date",
        [
            T("line_id", "Line ID"), T("receipt_id", "Receipt ID"), T("receipt_number", "Receipt number"), D("receipt_date", "Receipt date"),
            Supplier(), N("line_number", "Line number"), Item(), T("item_code", "Item code"), T("description", "Description"),
            N("quantity_received", "Quantity received"), T("unit", "Unit"), Warehouse(), T("purchase_order_line_id", "Purchase order line ID"),
            N("unit_cost", "Unit cost"), N("net_amount", "Net amount"), Project()
        ]),

        new(Shipments, "Shipments", "Sales documents whose type is a delivery note (goods shipped).", "shipment_date",
        [
            T("shipment_id", "Shipment ID"), T("shipment_number", "Shipment number"), T("document_type", "Document type"),
            Customer(), T("customer_name", "Customer"), D("shipment_date", "Shipment date"), T("sales_order_id", "Sales order ID"),
            Warehouse(), T("status", "Status"), F("cancelled", "Cancelled"), T("carrier", "Carrier"),
            T("tracking_number", "Tracking number"), Project(), N("total_amount", "Total amount"), T("currency", "Currency")
        ]),

        new(ShipmentLines, "Shipment lines", "Item lines of shipments. One Soft1 call per shipment.", "shipment_date",
        [
            T("line_id", "Line ID"), T("shipment_id", "Shipment ID"), T("shipment_number", "Shipment number"),
            D("shipment_date", "Shipment date"), Customer(), N("line_number", "Line number"), Item(), T("item_code", "Item code"),
            T("description", "Description"), N("quantity_shipped", "Quantity shipped"), T("unit", "Unit"),
            T("sales_order_line_id", "Sales order line ID"), Warehouse(), Project()
        ]),

        new(ItemSupplierPrices, "Item supplier prices",
            "Supplier item codes (`MTRSUPCODE`) with the item's last purchase price and date for the current fiscal year.", null,
        [
            Item(), T("item_code", "Item code"), Supplier(), T("supplier_item_code", "Supplier item code"),
            T("supplier_item_name", "Supplier item name"), N("agreement_price", "Agreement price"),
            N("last_purchase_price", "Last purchase price"), T("currency", "Currency"), D("valid_from", "Valid from"),
            D("valid_to", "Valid to"), D("last_purchase_date", "Last purchase date"), N("minimum_quantity", "Minimum quantity"),
            F("active", "Active")
        ]),

        new(SalesByCustomer, "Sales by customer",
            "Per month, customer and currency: invoiced sales (net), number of invoices. Built from sales documents.",
            "period_start",
        [
            T("period", "Period"), D("period_start", "Period start"), Customer(), T("customer_name", "Customer"), Salesperson(),
            T("currency", "Currency"), N("sales_amount", "Sales amount"), N("gross_margin", "Gross margin"),
            N("invoice_count", "Invoices"), N("quantity", "Quantity")
        ]),

        new(SalesByItem, "Sales by item",
            "Per month, item and currency: quantity and net sales from invoice lines. One Soft1 call per invoice.", "period_start",
        [
            T("period", "Period"), D("period_start", "Period start"), Item(), T("item_code", "Item code"), T("description", "Description"),
            N("quantity", "Quantity"), N("sales_amount", "Sales amount"), N("cost_amount", "Cost amount"),
            N("gross_margin", "Gross margin"), T("currency", "Currency")
        ]),

        new(SalesBySalesperson, "Sales by salesperson",
            "Per month, salesperson and currency: net sales, customers, orders and invoices.", "period_start",
        [
            T("period", "Period"), D("period_start", "Period start"), Salesperson(), T("salesperson_name", "Salesperson"),
            N("sales_amount", "Sales amount"), N("gross_margin", "Gross margin"), N("customer_count", "Customers"),
            N("order_count", "Orders"), N("invoice_count", "Invoices"), T("currency", "Currency")
        ]),

        new(CustomerCreditStatus, "Customer credit status",
            "Per customer: credit limit against balance plus open orders, and the overdue part.", null,
        [
            Customer(), T("name", "Name"), N("credit_limit", "Credit limit"), N("current_balance", "Current balance"),
            N("open_orders", "Open orders"), N("available_credit", "Available credit"), N("overdue_amount", "Overdue amount"),
            N("oldest_overdue_days", "Oldest overdue, days"), T("credit_status", "Credit status")
        ]),

        new(Projects, "Projects", "Projects (`PRJC`).", null,
        [
            Project(), T("project_code", "Project code"), T("name", "Name"), Customer(), T("customer_name", "Customer"),
            T("status", "Status"), T("stage", "Stage"), Salesperson("manager_id", "Manager ID"), D("start_date", "Start date"),
            D("target_date", "Target date"), D("completion_date", "Completion date"), N("contract_value", "Contract value"),
            T("currency", "Currency"), T("category", "Category"), F("active", "Active")
        ]),

        new(ProjectTransactions, "Project transactions",
            "Customer and supplier postings (`TRDTRN`) that carry a project.", "transaction_date",
        [
            T("transaction_id", "Transaction ID"), Project(), D("transaction_date", "Transaction date"),
            T("transaction_type", "Transaction type"), T("side", "Side"), T("document_id", "Document ID"),
            T("document_number", "Document number"), Customer(), Supplier(), N("amount", "Amount"),
            N("amount_local", "Amount in company currency"), T("currency", "Currency"), Account(), T("description", "Description")
        ]),

        new(ProjectFinancials, "Project financials",
            "Per project, in company currency, from its customer and supplier postings: invoiced, received, cost, paid.", null,
        [
            Project(), T("name", "Name"), N("revenue", "Revenue"), N("invoiced_revenue", "Invoiced revenue"),
            N("received_revenue", "Received revenue"), N("committed_cost", "Committed cost"), N("actual_cost", "Actual cost"),
            N("paid_cost", "Paid cost"), N("gross_margin", "Gross margin"), T("currency", "Currency")
        ]),

        new(Salespeople, "Salespeople", "Persons registered as salespersons (`PRSN` with entity 30).", null,
        [
            Salesperson(), T("code", "Code"), T("name", "Name"), T("email", "Email"), T("team", "Team"),
            Salesperson("manager_id", "Manager ID"), F("active", "Active")
        ]),

        new(PaymentTerms, "Payment terms", "Payment methods/terms (`PAYMENT`).", null,
        [
            T("payment_term_id", "Payment term ID"), T("code", "Code"), T("name", "Name"), N("days", "Days"),
            T("type", "Type"), N("instalments", "Instalments"), F("active", "Active")
        ]),

        new(DocumentTypes, "Document types", "Document types (`FPRMS`) with the module they belong to.", null,
        [
            T("document_type_id", "Document type ID"), T("code", "Code"), T("name", "Name"), T("category", "Category"),
            T("direction", "Direction"), N("module", "Module (SOSOURCE)"), N("behaviour", "Behaviour (TFPRMS)"),
            F("is_delivery_note", "Delivery note"), F("affects_stock", "Affects stock"), F("affects_accounting", "Affects accounting"),
            F("active", "Active")
        ]),

        new(Currencies, "Currencies", "Currencies (`SOCURRENCY`).", null,
        [
            T("currency_id", "Currency ID"), T("code", "Code"), T("name", "Name"), T("symbol", "Symbol"),
            T("international_code", "International code"), F("active", "Active")
        ]),

        new(Branches, "Branches", "Branches of the company (`BRANCH`).", null,
        [
            Branch(), T("branch_code", "Branch code"), T("name", "Name"), T("company_id", "Company ID"), T("location", "Location"),
            F("active", "Active")
        ]),

        new(Companies, "Companies", "The company this deployment signs in to (`COMPANY`).", null,
        [
            T("company_id", "Company ID"), T("company_code", "Company code"), T("name", "Name"), T("vat_number", "VAT number"),
            T("base_currency", "Base currency"), T("country", "Country"), F("active", "Active")
        ]),

        new(ItemCategories, "Item categories", "Item commercial categories (`MTRCATEGORY`).", null,
            [.. SimpleReference("category_id", "Category ID"), T("parent_category_id", "Parent category ID")]),

        new(CustomerCategories, "Customer categories", "Customer categories (`TRDCATEGORY`, entity 13).", null,
            SimpleReference("category_id", "Category ID")),

        new(SupplierCategories, "Supplier categories", "Supplier categories (`TRDCATEGORY`, entity 12).", null,
            SimpleReference("category_id", "Category ID")),

        new(TaxCodes, "Tax codes", "VAT codes (`VAT`).", null,
            [T("tax_code_id", "Tax code ID"), T("code", "Code"), T("name", "Name"), N("rate", "Rate, %"), T("tax_type", "Tax type"), F("active", "Active")]),

        new(Units, "Units", "Units of measure (`MTRUNIT`).", null,
            [T("unit_id", "Unit ID"), T("code", "Code"), T("name", "Name"), N("conversion_factor", "Conversion factor"), F("active", "Active")])
    ];

    public static readonly IReadOnlyDictionary<string, Soft1Source> ById =
        All.ToDictionary(static source => source.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A row carrying exactly the source's fields — a missing one reads null, and nothing a loader kept for its
    /// own joins leaks out.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> Project(Soft1Source source, IReadOnlyDictionary<string, object?> row)
    {
        var projected = new Dictionary<string, object?>(source.Fields.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var field in source.Fields)
        {
            projected[field.Name] = row.TryGetValue(field.Name, out var value) ? value : null;
        }

        return projected;
    }
}
