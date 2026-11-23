using Intera.ZohoInventory.Api.Clients;
using Intera.ZohoInventory.Api.Infrastructure;
using Intera.ZohoInventory.Api.Options;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Intera.ZohoInventory.Api.Services;

/// <summary>The organization this deployment reads, as <c>/api/Organization</c> reports it.</summary>
public sealed record ZohoOrganizationDto(
    string? OrganizationId,
    string? Name,
    string? CurrencyCode,
    string? TimeZone,
    string? Country,
    bool? IsActive);

/// <summary>
/// Per source (keyed by the API's own source names, e.g. <c>sales_order_lines</c>), the custom-field
/// columns that source carries in this organization.
/// </summary>
public sealed record ZohoSchemaDto(IReadOnlyDictionary<string, IReadOnlyList<ZohoCustomFieldDescriptor>> CustomFields);

public interface ISchemaService
{
    Task<ZohoOrganizationDto> GetOrganizationAsync(CancellationToken cancellationToken);

    Task<ZohoSchemaDto> GetSchemaAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reports the organization and its custom fields, for the plugin's <c>provisionInstance</c> and
/// <c>testConnection</c> hooks.
/// </summary>
/// <remarks>
/// <para>
/// <b>Custom fields come from <c>GET /settings/customfields</c>, which the Inventory API does not
/// document.</b> The first version sampled records instead, and a live organization showed why that
/// cannot work: Zoho leaves an empty custom field out of a record's <c>custom_fields</c> array
/// altogether (measured 2026-09-14: two sales order custom fields defined, a sales order carrying
/// <c>"custom_fields": []</c>), so a field nobody had filled in on the sampled records was invisible.
/// The settings endpoint answers with every definition per entity in one call, under the scope the
/// deployment already needs for locations (<c>settings.READ</c>), with the same
/// <c>api_name</c>/<c>label</c>/<c>data_type</c> keys a record uses — so both go through
/// <see cref="ZohoCustomFields"/> and cannot name a field two ways.
/// </para>
/// <para>
/// Being undocumented, it can change. When it does, provisioning fails with Zoho's answer and the
/// instance save is refused — visible, rather than a field list that quietly stops growing.
/// </para>
/// <para>
/// <b>Line custom fields are mapped by assumption.</b> A sales order, purchase order or receive line
/// carries <c>item_custom_fields</c>, and the only entity those can be is <c>item</c> (there is no
/// <c>salesorder_item</c>); package lines have their own <c>package_item</c> entity. The test
/// organization has no line custom fields at all, so neither mapping has been seen with data.
/// </para>
/// </remarks>
public sealed class SchemaService(
    IZohoInventoryClient client,
    IOptions<ZohoInventoryOptions> options) : ISchemaService
{
    private readonly ZohoInventoryOptions options = options.Value;

    public async Task<ZohoOrganizationDto> GetOrganizationAsync(CancellationToken cancellationToken)
    {
        var organization = await client
            .GetAsync($"organizations/{Uri.EscapeDataString(options.OrganizationId)}", "organization", cancellationToken)
            .ConfigureAwait(false);

        return new ZohoOrganizationDto(
            ZohoJson.Text(organization, "organization_id"),
            ZohoJson.Text(organization, "name"),
            ZohoJson.Text(organization, "currency_code"),
            ZohoJson.Text(organization, "time_zone"),
            ZohoJson.Text(ZohoJson.Property(organization, "address") ?? default, "country"),
            ZohoJson.Bool(organization, "is_org_active"));
    }

    public async Task<ZohoSchemaDto> GetSchemaAsync(CancellationToken cancellationToken)
    {
        var definitions = await client.GetAsync("settings/customfields", "customfields", cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ZohoCustomFieldDescriptor> Document(string entity) =>
            [.. ZohoCustomFields.Describe(definitions, entity, ZohoCustomFields.DocumentPrefix).DistinctBy(static field => field.Name)];

        IReadOnlyList<ZohoCustomFieldDescriptor> Line(string entity) =>
            [.. ZohoCustomFields.Describe(definitions, entity, ZohoCustomFields.LinePrefix).DistinctBy(static field => field.Name)];

        var packageLines = Union(Document("package"), Line("package_item"));

        var result = new Dictionary<string, IReadOnlyList<ZohoCustomFieldDescriptor>>(StringComparer.Ordinal)
        {
            // A vendor's custom fields are defined under their own entity, with their own field ids,
            // even though Zoho keeps customers and vendors in one /contacts resource.
            ["customers"] = Document("contact"),
            ["vendors"] = Document("vendor"),
            ["items"] = Document("item"),
            ["sales_orders"] = Document("salesorder"),
            ["sales_order_lines"] = Union(Document("salesorder"), Line("item")),
            ["purchase_orders"] = Document("purchaseorder"),
            ["purchase_order_lines"] = Union(Document("purchaseorder"), Line("item")),
            ["purchase_receives"] = Document("purchase_receive"),
            ["purchase_receive_lines"] = Union(Document("purchase_receive"), Line("item")),
            ["packages"] = Document("package"),
            ["package_lines"] = packageLines,
            ["shipment_lines"] = packageLines,
            ["contacts"] = Document("contact_person"),
            ["invoices"] = Document("invoice"),
            ["invoice_lines"] = Union(Document("invoice"), Line("item")),
            ["bills"] = Document("bill"),
            ["bill_lines"] = Union(Document("bill"), Line("item"))
        };

        return new ZohoSchemaDto(result);
    }

    private static IReadOnlyList<ZohoCustomFieldDescriptor> Union(
        IReadOnlyList<ZohoCustomFieldDescriptor> first,
        IReadOnlyList<ZohoCustomFieldDescriptor> second)
    {
        return [.. first.Concat(second).DistinctBy(static field => field.Name, StringComparer.Ordinal)];
    }
}
