using Intera.BusinessCentral.Api.Models;

namespace Intera.BusinessCentral.Api.Clients;

public interface IBusinessCentralClient
{
    Task<IReadOnlyList<BusinessCentralCompanyDto>> GetCompaniesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BusinessCentralCustomerDto>> GetCustomersAsync(string companyId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BusinessCentralVendorDto>> GetVendorsAsync(string companyId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BusinessCentralSalesInvoiceDto>> GetSalesInvoicesAsync(string companyId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BusinessCentralPurchaseInvoiceDto>> GetPurchaseInvoicesAsync(string companyId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BusinessCentralGeneralLedgerEntryDto>> GetGeneralLedgerEntriesAsync(string companyId, CancellationToken cancellationToken = default);
}
