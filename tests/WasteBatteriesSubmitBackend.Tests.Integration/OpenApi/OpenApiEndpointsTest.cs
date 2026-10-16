using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using WasteBatteriesSubmitBackend.OpenApi;
using WasteBatteriesSubmitBackend.Tests.Integration.Utils;

namespace WasteBatteriesSubmitBackend.Tests.Integration.OpenApi;

public class OpenApiEndpointsTest
{
    [Fact]
    public async Task OpenApi_document_is_available_without_a_bearer_token()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("/db-demo", document, StringComparison.Ordinal);
        Assert.Contains("/example", document, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(document);
        var info = json.RootElement.GetProperty("info");
        Assert.Equal("Waste Batteries Submit API", info.GetProperty("title").GetString());
        Assert.Equal("v1", info.GetProperty("version").GetString());
        Assert.Equal("Manage waste batteries submissions.", info.GetProperty("description").GetString());
    }

    [Fact]
    public async Task OpenApi_document_defines_a_bearer_scheme_on_authorized_operations()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", cancellationToken);
        var document = await response.Content.ReadAsStringAsync(cancellationToken);
        using var json = JsonDocument.Parse(document);
        var root = json.RootElement;

        var bearer = root.GetProperty("components").GetProperty("securitySchemes").GetProperty(BearerSecuritySchemeTransformer.SchemeId);
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());
        Assert.Equal(
            BearerSecuritySchemeTransformer.SchemeId,
            root.GetProperty("security")[0].EnumerateObject().Single().Name);
        Assert.False(root.GetProperty("paths").GetProperty("/db-demo").GetProperty("post").TryGetProperty("security", out _));
        Assert.False(root.GetProperty("paths").GetProperty("/example").GetProperty("get").TryGetProperty("security", out _));
    }

    [Fact]
    public async Task Root_redirects_to_swagger_ui_without_a_bearer_token()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/", cancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/swagger", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Swagger_ui_is_available_without_a_bearer_token()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/index.html", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed class TestApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestAuthentication();
        }
    }
}
