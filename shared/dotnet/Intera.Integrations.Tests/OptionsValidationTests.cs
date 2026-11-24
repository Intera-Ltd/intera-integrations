using Intera.BusinessCentral.Api.Options;
using Intera.Common.Authentication;
using Intera.Common.Infrastructure.Http;
using Intera.Eurobank.Api.Options;
using Intera.Jira.Api.Options;
using Intera.Revolut.Api.Options;
using Intera.Salesforce.Api.Options;
using Intera.Salesforce.Api.Services;
using Intera.Soft1.Api.Options;
using Intera.TeamCity.Api.Options;
using Intera.Unleashed.Api.Options;
using Intera.Xero.Api.Options;
using Intera.Zendesk.Api.Options;
using Intera.ZohoInventory.Api.Options;
using Intera.ZohoInventory.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Security.Cryptography;
using Xunit;

namespace Intera.Integrations.Tests;

public sealed class OptionsValidationTests
{
    [Fact]
    public void ServiceSecretsValidator_RequiresServiceTokenAlwaysAndDataKeyWhenStateful()
    {
        var validator = new ServiceSecretsOptionsValidator();

        Assert.True(validator.Validate(null, new ServiceSecretsOptions()).Failed);
        Assert.True(validator.Validate(null, new ServiceSecretsOptions
        {
            ServiceToken = "tok",
            RequiresDataKey = false
        }).Succeeded);

        Assert.True(validator.Validate(null, new ServiceSecretsOptions
        {
            ServiceToken = "tok",
            RequiresDataKey = true
        }).Failed);

        Assert.True(validator.Validate(null, new ServiceSecretsOptions
        {
            ServiceToken = "tok",
            RequiresDataKey = true,
            DataKey = Convert.ToBase64String(new byte[16])
        }).Failed);

        Assert.True(validator.Validate(null, new ServiceSecretsOptions
        {
            ServiceToken = "tok",
            RequiresDataKey = true,
            DataKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }).Succeeded);
    }

    [Fact]
    public void AllElevenValidators_FailWhenRequiredSettingsMissingPathTraversesOrPlaintextHttpUsed()
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
            OAuthTokenFilePath = "App_Data/../secret.json"
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

        var revolutValidator = new RevolutOptionsValidator();
        Assert.True(revolutValidator.Validate(null, new RevolutOptions()).Failed);
        Assert.True(revolutValidator.Validate(null, new RevolutOptions
        {
            ClientId = "id",
            ClientAssertionIssuer = "core.example.com",
            PrivateKeyPem = "smoke-test",
            ApiBaseUrl = "http://b2b.revolut.com/api/1.0"
        }).Failed);
        Assert.True(revolutValidator.Validate(null, new RevolutOptions
        {
            ClientId = "id",
            ClientAssertionIssuer = "core.example.com",
            PrivateKeyPem = "smoke-test",
            OAuthTokenFilePath = "../outside.json"
        }).Failed);

        var eurobankValidator = new EurobankOptionsValidator();
        Assert.True(eurobankValidator.Validate(null, new EurobankOptions()).Failed);
        Assert.True(eurobankValidator.Validate(null, new EurobankOptions
        {
            ClientId = "id",
            ClientSecret = "sec",
            ClientCertificatePem = "cert-only-without-key"
        }).Failed);

        var bcValidator = new BusinessCentralOptionsValidator();
        Assert.True(bcValidator.Validate(null, new BusinessCentralOptions()).Failed);
        Assert.True(bcValidator.Validate(null, new BusinessCentralOptions
        {
            TenantId = "00000000-0000-0000-0000-000000000001",
            Environment = "Production",
            ClientId = "id",
            ClientSecret = "sec",
            ApiBaseUrl = "http://api.businesscentral.dynamics.com"
        }).Failed);

        var unleashedValidator = new UnleashedOptionsValidator();
        Assert.True(unleashedValidator.Validate(null, new UnleashedOptions()).Failed);
        Assert.True(unleashedValidator.Validate(null, new UnleashedOptions
        {
            ApiId = "id",
            ApiKey = "key",
            ApiBaseUrl = "http://api.unleashedsoftware.com"
        }).Failed);

        var soft1Validator = new Soft1OptionsValidator();
        Assert.True(soft1Validator.Validate(null, new Soft1Options()).Failed);
        Assert.True(soft1Validator.Validate(null, new Soft1Options
        {
            ServiceUrl = "http://acme.oncloud.gr/s1services",
            Username = "u",
            Password = "p",
            AppId = "1001"
        }).Failed);

        var teamCityValidator = new TeamCityOptionsValidator();
        Assert.True(teamCityValidator.Validate(null, new TeamCityOptions()).Failed);
        Assert.True(teamCityValidator.Validate(null, new TeamCityOptions
        {
            ServerUrl = "http://teamcity.example.com",
            Token = "tok"
        }).Failed);
    }

    [Fact]
    public void AllElevenValidators_SucceedWithValidSettingsAndLoopbackTestUrls()
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

        Assert.True(new RevolutOptionsValidator().Validate(null, new RevolutOptions
        {
            ClientId = "id",
            ClientAssertionIssuer = "core.example.com",
            PrivateKeyPem = "smoke-test"
        }).Succeeded);

        Assert.True(new EurobankOptionsValidator().Validate(null, new EurobankOptions
        {
            ClientId = "id",
            ClientSecret = "sec"
        }).Succeeded);

        Assert.True(new BusinessCentralOptionsValidator().Validate(null, new BusinessCentralOptions
        {
            TenantId = "00000000-0000-0000-0000-000000000001",
            Environment = "Production",
            ClientId = "id",
            ClientSecret = "sec"
        }).Succeeded);

        Assert.True(new UnleashedOptionsValidator().Validate(null, new UnleashedOptions
        {
            ApiId = "id",
            ApiKey = "key"
        }).Succeeded);

        Assert.True(new Soft1OptionsValidator().Validate(null, new Soft1Options
        {
            ServiceUrl = "https://acme.oncloud.gr/s1services",
            Username = "u",
            Password = "p",
            AppId = "1001"
        }).Succeeded);

        Assert.True(new TeamCityOptionsValidator().Validate(null, new TeamCityOptions
        {
            ServerUrl = "https://teamcity.example.com",
            Token = "tok"
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
