using Intera.ZohoInventory.Api.Clients;
using Intera.ZohoInventory.Api.Infrastructure;
using Intera.ZohoInventory.Api.Models.Query;
using System.Text.Json;

namespace Intera.ZohoInventory.Api.Services;

public interface ICatalogService
{
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetCustomerRowsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetVendorRowsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetItemRowsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetLocationRowsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetSalespersonRowsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The master lists — customers, vendors, items, locations, salespersons — each one list read.
/// </summary>
/// <remarks>
/// <para>
/// <b>Customers and vendors are one Zoho resource.</b> Zoho keeps both in <c>/contacts</c>, told
/// apart by <c>contact_type</c>. They are still two sources because they answer different questions
/// and carry different balances (receivable against payable), and a caller should not have to
/// remember a type filter to avoid adding a supplier's debt to a customer's.
/// </para>
/// <para>
/// The type is filtered here, not upstream: <c>/contacts</c> documents no <c>contact_type</c>
/// parameter, and a parameter Zoho ignored would silently return both kinds. <c>filter_by</c> is
/// sent as <c>Status.All</c> so inactive contacts are rows rather than a decision made for the
/// caller — an inactive customer still owns its orders.
/// </para>
/// <para>
/// These read the list only, so addresses, the website and the primary contact person — detail-only
/// fields — are not here. Reading them would cost one call per contact on every lookup Core makes to
/// label a <c>customer_id</c>.
/// </para>
/// </remarks>
public sealed class CatalogService(IZohoInventoryClient client) : ICatalogService
{
    private static readonly KeyValuePair<string, string?>[] AllStatuses = [new("filter_by", "Status.All")];

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetCustomerRowsAsync(CancellationToken cancellationToken)
    {
        var contacts = await ReadContactsAsync("customer", cancellationToken).ConfigureAwait(false);
        return [.. contacts.Select(static contact => (IReadOnlyDictionary<string, object?>)ZohoRows.Customer(contact))];
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetVendorRowsAsync(CancellationToken cancellationToken)
    {
        var contacts = await ReadContactsAsync("vendor", cancellationToken).ConfigureAwait(false);
        return [.. contacts.Select(static contact => (IReadOnlyDictionary<string, object?>)ZohoRows.Vendor(contact))];
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetItemRowsAsync(CancellationToken cancellationToken)
    {
        var items = await client.ListAllAsync("items", "items", AllStatuses, cancellationToken).ConfigureAwait(false);
        return [.. items.Select(static item => (IReadOnlyDictionary<string, object?>)ZohoRows.Item(item))];
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetLocationRowsAsync(CancellationToken cancellationToken)
    {
        var locations = await client.ListAllAsync("locations", "locations", [], cancellationToken).ConfigureAwait(false);
        return [.. locations.Select(static location => (IReadOnlyDictionary<string, object?>)ZohoRows.Location(location))];
    }

    /// <summary>
    /// <c>GET /salespersons</c> is not in the Inventory API documentation, which says no such
    /// resource exists; measured 2026-09-18, it answers under <c>data</c>, unpaginated (page 2 repeats
    /// page 1), with each salesperson's id, name, email, mobile and <c>is_active</c>. It is used over
    /// collecting names off the documents because the sales order list carries
    /// <c>salesperson_name</c> but no <c>salesperson_id</c>, and a salesperson with no documents yet
    /// would be missing. Like <c>/settings/customfields</c>, a change on Zoho's side fails loudly.
    /// </summary>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetSalespersonRowsAsync(CancellationToken cancellationToken)
    {
        var salespersons = await client.GetCollectionAsync("salespersons", "data", [], cancellationToken).ConfigureAwait(false);
        return [.. salespersons.Select(static salesperson => (IReadOnlyDictionary<string, object?>)ZohoRows.Salesperson(salesperson))];
    }

    private async Task<IReadOnlyList<JsonElement>> ReadContactsAsync(string contactType, CancellationToken cancellationToken)
    {
        var contacts = await client.ListAllAsync("contacts", "contacts", AllStatuses, cancellationToken).ConfigureAwait(false);

        return
        [
            .. contacts.Where(contact => string.Equals(ZohoJson.Text(contact, "contact_type"), contactType, StringComparison.OrdinalIgnoreCase))
        ];
    }
}
