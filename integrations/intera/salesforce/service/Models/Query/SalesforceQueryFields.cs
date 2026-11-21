using Intera.Salesforce.Api.Infrastructure.Soql;
using static Intera.Salesforce.Api.Infrastructure.Soql.SoqlField;
using static Intera.Salesforce.Api.Infrastructure.Soql.SoqlFieldUse;

namespace Intera.Salesforce.Api.Models.Query;

/// <summary>
/// The field allowlists for the six Salesforce endpoints — the sObject each one queries and every
/// column a caller may name.
/// </summary>
/// <remarks>
/// Every name here must also exist in the matching descriptor list in
/// <c>intera-salesforce-integration</c>: the builder rejects a field it does not know, so a rename
/// has to happen on both sides at once.
/// <para>
/// <b>Where a field may be used is data, not decoration.</b> Each entry's <see cref="SoqlFieldUse"/>
/// was read from <c>/sobjects/{object}/describe</c> and then confirmed one query at a time against a
/// live org (2026-09-03) — Salesforce genuinely refuses <c>GROUP BY Amount</c>, <c>GROUP BY
/// CreatedDate</c>, <c>GROUP BY CaseNumber</c>, <c>SUM</c> of a checkbox and <c>MIN(ActivityDate)</c>
/// on an activity, while allowing all of those fields in a <c>WHERE</c>. The helper that builds each
/// entry carries the rule for its type; the handful of fields whose flags do not follow their type
/// say so with an explicit <c>.Without(...)</c>.
/// </para>
/// <para>
/// <b>Standard fields only, deliberately.</b> A Salesforce org's real value is often in its custom
/// <c>__c</c> fields, and they are not here: their names differ per org, so putting them in a list
/// compiled into an image would mean one image per customer. Adding them properly means reading
/// describe at runtime and letting the plugin's field descriptors follow — a real feature, and a
/// different one from this.
/// </para>
/// <para>
/// <b>Deleted records are not here either.</b> The client queries <c>/query/</c>, not
/// <c>/queryAll/</c>, so records in the Recycle Bin and archived activities are excluded — which is
/// what a dashboard means by "our accounts". There is consequently no <c>is_deleted</c> column: it
/// would read <see langword="false"/> on every row.
/// </para>
/// </remarks>
public static class AccountQueryFields
{
    public static readonly SoqlObject Source = new("Account",
    [
        Text("account_id", "Id"),
        Text("account_name", "Name"),
        Text("account_number", "AccountNumber"),
        Text("account_type", "Type"),
        Text("industry", "Industry"),
        Text("rating", "Rating"),
        Text("ownership", "Ownership"),
        Decimal("annual_revenue", "AnnualRevenue"),
        Number("employees", "NumberOfEmployees"),
        Text("phone", "Phone"),
        Text("fax", "Fax"),
        Text("website", "Website"),
        Text("billing_street", "BillingStreet"),
        Text("billing_city", "BillingCity"),
        Text("billing_state", "BillingState"),
        Text("billing_postal_code", "BillingPostalCode"),
        Text("billing_country", "BillingCountry"),
        Text("shipping_city", "ShippingCity"),
        Text("shipping_state", "ShippingState"),
        Text("shipping_country", "ShippingCountry"),
        Text("parent_account_id", "ParentId"),
        Text("parent_account_name", "Parent.Name"),
        Text("owner_id", "OwnerId"),
        Text("owner_name", "Owner.Name"),
        LongText("description", "Description"),
        Day("last_activity_date", "LastActivityDate"),
        Timestamp("created_date", "CreatedDate"),
        Timestamp("last_modified_date", "LastModifiedDate")
    ]);
}

public static class OpportunityQueryFields
{
    public static readonly SoqlObject Source = new("Opportunity",
    [
        Text("opportunity_id", "Id"),
        Text("opportunity_name", "Name"),
        Text("account_id", "AccountId"),
        Text("account_name", "Account.Name"),
        Decimal("amount", "Amount"),
        Decimal("expected_revenue", "ExpectedRevenue"),
        Decimal("quantity", "TotalOpportunityQuantity"),

        // StageName is the pipeline stage's own name; is_closed / is_won are Salesforce's derived
        // flags off the same stage, and they are the honest way to ask "won this quarter" — the set
        // of stage names is configured per org, so matching on the text is a per-org guess.
        Text("stage", "StageName"),
        Decimal("probability", "Probability"),
        Checkbox("is_closed", "IsClosed"),
        Checkbox("is_won", "IsWon"),
        Text("forecast_category", "ForecastCategoryName"),
        Text("opportunity_type", "Type"),
        Text("lead_source", "LeadSource"),
        Text("next_step", "NextStep"),
        Text("campaign_id", "CampaignId"),
        Checkbox("has_line_items", "HasOpportunityLineItem"),
        Number("fiscal_year", "FiscalYear"),
        Number("fiscal_quarter", "FiscalQuarter"),
        Text("owner_id", "OwnerId"),
        Text("owner_name", "Owner.Name"),
        LongText("description", "Description"),
        Day("close_date", "CloseDate"),
        Day("last_activity_date", "LastActivityDate"),
        Timestamp("created_date", "CreatedDate"),
        Timestamp("last_modified_date", "LastModifiedDate")
    ]);
}

public static class CaseQueryFields
{
    public static readonly SoqlObject Source = new("Case",
    [
        Text("case_id", "Id"),

        // An auto-number, and Salesforce does not group by one — it is unique per row, so a group
        // key made of it is a row key.
        Text("case_number", "CaseNumber").Without(Group),
        Text("subject", "Subject"),
        Text("status", "Status"),
        Text("priority", "Priority"),
        Text("origin", "Origin"),
        Text("case_type", "Type"),
        Text("reason", "Reason"),
        Checkbox("is_closed", "IsClosed"),
        Checkbox("is_escalated", "IsEscalated"),
        Text("account_id", "AccountId"),
        Text("account_name", "Account.Name"),
        Text("contact_id", "ContactId"),
        Text("contact_name", "Contact.Name"),
        Text("contact_email", "Contact.Email"),
        Text("owner_id", "OwnerId"),

        // Case.Owner is polymorphic — a user or a queue — and Name exists on both, so the traversal
        // works everywhere. Verified against a live org rather than assumed.
        Text("owner_name", "Owner.Name"),
        LongText("description", "Description"),
        Timestamp("closed_date", "ClosedDate"),
        Timestamp("created_date", "CreatedDate"),
        Timestamp("last_modified_date", "LastModifiedDate")
    ]);
}

/// <summary>
/// Tasks: calls, e-mails and to-dos (one half of Salesforce activities alongside Events).
/// </summary>
/// <remarks>
/// Task and Event are two data sources rather than one, because they are two objects with two field
/// sets: a Task carries a status, a priority and a call duration, an Event a start, an end and a
/// duration in minutes, and the only real overlap is the subject, the date, the owner and the two
/// polymorphic links. A single union source would leave half of every row null and could not be
/// paged honestly — one <c>LIMIT</c> cannot bound two queries.
/// </remarks>
public static class TaskQueryFields
{
    public static readonly SoqlObject Source = new("Task",
    [
        Text("task_id", "Id"),

        // A combobox — free text with suggestions — which Salesforce declines to group by.
        Text("subject", "Subject").Without(Group),
        Text("status", "Status"),
        Text("priority", "Priority"),
        Checkbox("is_closed", "IsClosed"),
        Checkbox("is_high_priority", "IsHighPriority"),
        Text("subtype", "TaskSubtype"),
        Text("call_type", "CallType"),
        Text("call_disposition", "CallDisposition"),
        Number("call_duration_seconds", "CallDurationInSeconds"),
        Checkbox("is_recurrence", "IsRecurrence"),
        Text("owner_id", "OwnerId"),
        Text("owner_name", "Owner.Name"),

        // Who = the person (a contact or a lead), What = the record it is about (an account, an
        // opportunity, a case, …). Both traversals were expected to be unusable in a WHERE, since
        // "what" can point at a Case, which has a number rather than a name — a live org says
        // otherwise, and Salesforce accepts all four clauses on them. Measured beats reasoned.
        Text("who_id", "WhoId"),
        Text("who_name", "Who.Name"),
        Text("what_id", "WhatId"),
        Text("what_name", "What.Name"),
        Text("account_id", "AccountId"),
        LongText("description", "Description"),

        // A Date, and groupable — but an activity's date is the one date field Salesforce refuses to
        // aggregate, so no MIN/MAX of it.
        Day("activity_date", "ActivityDate").Without(Aggregate),
        Timestamp("created_date", "CreatedDate"),
        Timestamp("last_modified_date", "LastModifiedDate")
    ]);
}

/// <summary>Events: meetings and anything else with a start and an end.</summary>
public static class EventQueryFields
{
    public static readonly SoqlObject Source = new("Event",
    [
        Text("event_id", "Id"),
        Text("subject", "Subject").Without(Group),
        Text("location", "Location"),
        Text("show_as", "ShowAs"),
        Text("subtype", "EventSubtype"),
        Checkbox("is_all_day", "IsAllDayEvent"),
        Checkbox("is_private", "IsPrivate"),
        Checkbox("is_recurrence", "IsRecurrence"),
        Number("duration_minutes", "DurationInMinutes"),
        Text("owner_id", "OwnerId"),
        Text("owner_name", "Owner.Name"),
        Text("who_id", "WhoId"),
        Text("who_name", "Who.Name"),
        Text("what_id", "WhatId"),
        Text("what_name", "What.Name"),
        Text("account_id", "AccountId"),
        LongText("description", "Description"),

        // ActivityDate is the day the event starts and is the field the two activity sources share;
        // start_at / end_at carry the time, which a Task has no equivalent of.
        Day("activity_date", "ActivityDate").Without(Aggregate),
        Timestamp("start_at", "StartDateTime"),
        Timestamp("end_at", "EndDateTime"),
        Timestamp("created_date", "CreatedDate"),
        Timestamp("last_modified_date", "LastModifiedDate")
    ]);
}

/// <summary>
/// Orders. Note that Orders is a Salesforce feature an org can have switched off — the endpoint then
/// answers 400 with Salesforce's own <c>INVALID_TYPE</c> text rather than an empty list, which is
/// the honest answer: the object does not exist there, it is not that there are no orders.
/// </summary>
public static class OrderQueryFields
{
    public static readonly SoqlObject Source = new("Order",
    [
        Text("order_id", "Id"),
        Text("order_number", "OrderNumber").Without(Group),
        Text("account_id", "AccountId"),
        Text("account_name", "Account.Name"),
        Text("contract_id", "ContractId"),
        Text("status", "Status"),

        // Status is the org's own picklist label; StatusCode is the restricted two-value form
        // (Draft / Activated) every org shares, and is what a cross-org dashboard should filter on.
        Text("status_code", "StatusCode"),
        Text("order_type", "Type"),
        Decimal("total_amount", "TotalAmount"),
        Text("po_number", "PoNumber"),
        Text("order_reference_number", "OrderReferenceNumber"),
        Checkbox("is_reduction_order", "IsReductionOrder"),
        Text("billing_city", "BillingCity"),
        Text("billing_country", "BillingCountry"),
        Text("shipping_city", "ShippingCity"),
        Text("shipping_country", "ShippingCountry"),
        Text("owner_id", "OwnerId"),
        Text("owner_name", "Owner.Name"),
        LongText("description", "Description"),
        Day("effective_date", "EffectiveDate"),
        Day("end_date", "EndDate"),
        Day("po_date", "PoDate"),
        Timestamp("activated_date", "ActivatedDate"),
        Timestamp("created_date", "CreatedDate"),
        Timestamp("last_modified_date", "LastModifiedDate")
    ]);
}
