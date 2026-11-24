using System.Diagnostics.CodeAnalysis;
using Intera.BusinessCentral.Api.Clients;
using Intera.BusinessCentral.Api.Models.Query;
using Intera.Common.Authentication;
using Intera.Common.Controllers;
using Intera.Common.Models.Query;
using Intera.Common.Swagger;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Intera.BusinessCentral.Api.Controllers;

[RequireAccessToken]
[SwaggerManualToken]
public sealed class BusinessCentralController(IBusinessCentralClient client) : ApiControllerBase
{
    /// <summary>
    /// Companies in the tenant. Unscoped — every other endpoint on this controller takes the company's
    /// id (from a row here) as its {companyId} route segment.
    /// </summary>
    [HttpPost("companies/query")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryCompaniesAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        var companies = await client.GetCompaniesAsync(cancellationToken).ConfigureAwait(false);

        var rows = companies.Select(company => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = company.Id,
            ["name"] = company.Name,
            ["display_name"] = company.DisplayName,
            ["business_profile_id"] = company.BusinessProfileId
        });

        return QueryInMemoryResponse(rows, query, BusinessCentralCompanyQueryFields.All);
    }

    [HttpPost("{companyId}/customers/query")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryCustomersAsync(
        string companyId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        if (!TryValidateCompanyId(companyId, out var badRequest))
        {
            return badRequest;
        }

        var customers = await client.GetCustomersAsync(companyId, cancellationToken).ConfigureAwait(false);

        var rows = customers.Select(customer => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = customer.Id,
            ["number"] = customer.Number,
            ["display_name"] = customer.DisplayName,
            ["type"] = customer.Type,
            ["address_line1"] = customer.AddressLine1,
            ["city"] = customer.City,
            ["country"] = customer.Country,
            ["postal_code"] = customer.PostalCode,
            ["phone_number"] = customer.PhoneNumber,
            ["email"] = customer.Email,
            ["currency_code"] = customer.CurrencyCode,
            ["balance_due"] = customer.BalanceDue,
            ["credit_limit"] = customer.CreditLimit,
            ["blocked"] = customer.Blocked,
            ["tax_registration_number"] = customer.TaxRegistrationNumber,
            ["payment_terms_id"] = customer.PaymentTermsId,
            ["last_modified_date_time"] = customer.LastModifiedDateTime
        });

        return QueryInMemoryResponse(rows, query, BusinessCentralCustomerQueryFields.All);
    }

    [HttpPost("{companyId}/vendors/query")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryVendorsAsync(
        string companyId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        if (!TryValidateCompanyId(companyId, out var badRequest))
        {
            return badRequest;
        }

        var vendors = await client.GetVendorsAsync(companyId, cancellationToken).ConfigureAwait(false);

        var rows = vendors.Select(vendor => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = vendor.Id,
            ["number"] = vendor.Number,
            ["display_name"] = vendor.DisplayName,
            ["address_line1"] = vendor.AddressLine1,
            ["city"] = vendor.City,
            ["country"] = vendor.Country,
            ["postal_code"] = vendor.PostalCode,
            ["phone_number"] = vendor.PhoneNumber,
            ["email"] = vendor.Email,
            ["currency_code"] = vendor.CurrencyCode,
            ["balance"] = vendor.Balance,
            ["blocked"] = vendor.Blocked,
            ["tax_registration_number"] = vendor.TaxRegistrationNumber,
            ["payment_terms_id"] = vendor.PaymentTermsId,
            ["last_modified_date_time"] = vendor.LastModifiedDateTime
        });

        return QueryInMemoryResponse(rows, query, BusinessCentralVendorQueryFields.All);
    }

    [HttpPost("{companyId}/sales-invoices/query")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QuerySalesInvoicesAsync(
        string companyId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        if (!TryValidateCompanyId(companyId, out var badRequest))
        {
            return badRequest;
        }

        var invoices = await client.GetSalesInvoicesAsync(companyId, cancellationToken).ConfigureAwait(false);

        var rows = invoices.Select(invoice => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = invoice.Id,
            ["number"] = invoice.Number,
            ["external_document_number"] = invoice.ExternalDocumentNumber,
            ["invoice_date"] = invoice.InvoiceDate,
            ["posting_date"] = invoice.PostingDate,
            ["due_date"] = invoice.DueDate,
            ["customer_id"] = invoice.CustomerId,
            ["customer_number"] = invoice.CustomerNumber,
            ["customer_name"] = invoice.CustomerName,
            ["currency_code"] = invoice.CurrencyCode,
            ["status"] = invoice.Status,
            ["discount_amount"] = invoice.DiscountAmount,
            ["total_amount_excluding_tax"] = invoice.TotalAmountExcludingTax,
            ["total_tax_amount"] = invoice.TotalTaxAmount,
            ["total_amount_including_tax"] = invoice.TotalAmountIncludingTax,
            ["remaining_amount"] = invoice.RemainingAmount,
            ["last_modified_date_time"] = invoice.LastModifiedDateTime
        });

        return QueryInMemoryResponse(rows, query, BusinessCentralSalesInvoiceQueryFields.All);
    }

    [HttpPost("{companyId}/purchase-invoices/query")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryPurchaseInvoicesAsync(
        string companyId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        if (!TryValidateCompanyId(companyId, out var badRequest))
        {
            return badRequest;
        }

        var invoices = await client.GetPurchaseInvoicesAsync(companyId, cancellationToken).ConfigureAwait(false);

        var rows = invoices.Select(invoice => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = invoice.Id,
            ["number"] = invoice.Number,
            ["vendor_invoice_number"] = invoice.VendorInvoiceNumber,
            ["invoice_date"] = invoice.InvoiceDate,
            ["posting_date"] = invoice.PostingDate,
            ["due_date"] = invoice.DueDate,
            ["vendor_id"] = invoice.VendorId,
            ["vendor_number"] = invoice.VendorNumber,
            ["vendor_name"] = invoice.VendorName,
            ["currency_code"] = invoice.CurrencyCode,
            ["status"] = invoice.Status,
            ["total_amount_excluding_tax"] = invoice.TotalAmountExcludingTax,
            ["total_tax_amount"] = invoice.TotalTaxAmount,
            ["total_amount_including_tax"] = invoice.TotalAmountIncludingTax,
            ["remaining_amount"] = invoice.RemainingAmount,
            ["last_modified_date_time"] = invoice.LastModifiedDateTime
        });

        return QueryInMemoryResponse(rows, query, BusinessCentralPurchaseInvoiceQueryFields.All);
    }

    /// <summary>
    /// General ledger entries, newest-posted first, capped at BusinessCentral:MaxRecordsPerEntity. Only the
    /// standard flat fields are exposed — dimensions are a separate dimensionSetLines sub-collection in BC's
    /// stock API, not columns on the entry, so there is no dimension_1/dimension_2-style field here.
    /// </summary>
    [HttpPost("{companyId}/general-ledger-entries/query")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryGeneralLedgerEntriesAsync(
        string companyId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        if (!TryValidateCompanyId(companyId, out var badRequest))
        {
            return badRequest;
        }

        var entries = await client.GetGeneralLedgerEntriesAsync(companyId, cancellationToken).ConfigureAwait(false);

        var rows = entries.Select(entry => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = entry.Id,
            ["entry_number"] = entry.EntryNumber,
            ["posting_date"] = entry.PostingDate,
            ["document_number"] = entry.DocumentNumber,
            ["document_type"] = entry.DocumentType,
            ["account_id"] = entry.AccountId,
            ["account_number"] = entry.AccountNumber,
            ["description"] = entry.Description,
            ["debit_amount"] = entry.DebitAmount,
            ["credit_amount"] = entry.CreditAmount,
            ["additional_currency_debit_amount"] = entry.AdditionalCurrencyDebitAmount,
            ["additional_currency_credit_amount"] = entry.AdditionalCurrencyCreditAmount
        });

        return QueryInMemoryResponse(rows, query, BusinessCentralGeneralLedgerEntryQueryFields.All);
    }

    /// <summary>BC company ids are GUIDs; rejecting non-GUID input here avoids building a malformed OData path.</summary>
    private bool TryValidateCompanyId(string companyId, [NotNullWhen(false)] out ActionResult? badRequest)
    {
        if (Guid.TryParse(companyId, out _))
        {
            badRequest = null;
            return true;
        }

        badRequest = BadRequest(ApiResponse<object?>.Fail("companyId must be a valid GUID."));
        return false;
    }
}
