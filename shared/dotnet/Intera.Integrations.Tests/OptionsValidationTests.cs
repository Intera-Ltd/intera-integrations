using Intera.Common.Infrastructure.Http;
using Intera.Jira.Api.Options;
using Intera.Salesforce.Api.Options;
using Intera.Salesforce.Api.Services;
using Intera.Xero.Api.Options;
using Intera.Zendesk.Api.Options;
using Intera.ZohoInventory.Api.Options;
using Intera.ZohoInventory.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using Xunit;

namespace Intera.Integrations.Tests;

public sealed class OptionsValidationTests
{
    [Fact]
    public void AllFiveValidators_FailWhenRequiredSettingsMissingOrPlaintextHttpUsed()
    {
        var jiraValidator = new JiraOptionsValidator();
        Assert.True(jiraValidator.Validate(null, new JiraOptions()).Failed);
        Assert.True(jiraValidator.Validate(null, new JiraOptions
        {
            ClientId = "id",
            ClientSecret = "sec",
            TokenUrl = "http://auth.atlassian.com/oauth/token"
        }).Failed);
        Assert.True(jiraValidator.Validate(null, new JiraOptions
        {
            ClientId = "id",
            ClientSecret = "sec",
            SiteUrl = "http://acme.atlassian.net"
        }).Failed);

        var xeroValidator = new XeroOptionsValidator();
        Assert.True(xeroValidator.Validate(null, new XeroOptions()).Failed);
        Assert.True(xeroValidator.Validate(null, new XeroOptions
        {
            ClientId = "id",
            ClientSecret = "sec",
            ApiBaseUrl = "http://api.xero.com/api.xro/2.0"
        }).Failed);

        var zendeskValidator = new ZendeskOptionsValidator();
        Assert.True(zendeskValidator.Validate(null, new ZendeskOptions()).Failed);
        Assert.True(zendeskValidator.Validate(null, new ZendeskOptions
        {
            ClientId = "id",
            ClientSecret = "sec",
            BaseUrl = "http://acme.zendesk.com"
        }).Failed);

        var salesforceValidator = new SalesforceOptionsValidator();
        Assert.True(salesforceValidator.Validate(null, new SalesforceOptions()).Failed);
        Assert.True(salesforceValidator.Validate(null, new SalesforceOptions
        {
            ClientId = "id",
            ClientSecret = "sec",
            LoginUrl = "http://acme.my.salesforce.com"
        }).Failed);

        var zohoValidator = new ZohoInventoryOptionsValidator();
        Assert.True(zohoValidator.Validate(null, new ZohoInventoryOptions()).Failed);
        Assert.True(zohoValidator.Validate(null, new ZohoInventoryOptions
        {
            OrganizationId = "org-1",
            ClientId = "id",
            ClientSecret = "sec",
            RefreshToken = "ref",
            AccountsUrl = "http://accounts.zoho.com"
        }).Failed);
    }

    [Fact]
    public void AllFiveValidators_SucceedWithValidSettingsAndLoopbackTestUrls()
    {
        Assert.True(new JiraOptionsValidator().Validate(null, new JiraOptions
        {
            ClientId = "id",
            ClientSecret = "sec",
            SiteUrl = "11223344-5566-7788-99aa-bbccddeeff00"
        }).Succeeded);

        Assert.True(new XeroOptionsValidator().Validate(null, new XeroOptions
        {
            ClientId = "id",
            ClientSecret = "sec",
            RedirectUri = "http://localhost:8080/oauth/callback"
        }).Succeeded);

        Assert.True(new ZendeskOptionsValidator().Validate(null, new ZendeskOptions
        {
            Subdomain = "acme",
            ClientId = "id",
            ClientSecret = "sec"
        }).Succeeded);

        Assert.True(new SalesforceOptionsValidator().Validate(null, new SalesforceOptions
        {
            LoginUrl = "https://acme.my.salesforce.com",
            ClientId = "id",
            ClientSecret = "sec"
        }).Succeeded);

        Assert.True(new ZohoInventoryOptionsValidator().Validate(null, new ZohoInventoryOptions
        {
            OrganizationId = "org-1",
            ClientId = "id",
            ClientSecret = "sec",
            RefreshToken = "ref"
        }).Succeeded);
    }

    [Fact]
    public void ValidateOnStart_ThrowsBeforeStartupCompletesWhenOptionsAreInvalid()
    {
        var services = new ServiceCollection();
        services.AddOptions<SalesforceOptions>().ValidateOnStart();
        services.AddSingleton<IValidateOptions<SalesforceOptions>, SalesforceOptionsValidator>();

        using var provider = services.BuildServiceProvider();
        var startupValidator = provider.GetRequiredService<IStartupValidator>();

        Assert.Throws<OptionsValidationException>(() => startupValidator.Validate());
    }

    [Fact]
    public async Task SalesforceAndZohoOAuthServices_RejectPlaintextHttpHostsReturnedByTokenEndpoint()
    {
        var ct = TestContext.Current.CancellationToken;

        var sfHandler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            StubHttpMessageHandler.Json(
                HttpStatusCode.OK,
                """{"access_token":"sf-tok","instance_url":"http://insecure.salesforce.com"}""")));
        var sfService = new SalesforceOAuthService(
            Options.Create(new SalesforceOptions
            {
                LoginUrl = "https://acme.my.salesforce.com",
                ClientId = "id",
                ClientSecret = "sec"
            }),
            new JsonHttpClient(new HttpClient(sfHandler)),
            NullLogger<SalesforceOAuthService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sfService.GetAccessTokenAsync(ct));

        var zohoHandler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            StubHttpMessageHandler.Json(
                HttpStatusCode.OK,
                """{"access_token":"zoho-tok","api_domain":"http://www.zohoapis.com","expires_in":3600}""")));
        var zohoService = new ZohoOAuthService(
            Options.Create(new ZohoInventoryOptions
            {
                OrganizationId = "org-1",
                ClientId = "id",
                ClientSecret = "sec",
                RefreshToken = "ref"
            }),
            new JsonHttpClient(new HttpClient(zohoHandler)),
            NullLogger<ZohoOAuthService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => zohoService.GetAccessTokenAsync(ct));
    }
}
