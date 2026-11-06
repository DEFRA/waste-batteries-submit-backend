using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using WasteBatteriesSubmitBackend.Example.Repositories;

namespace WasteBatteriesSubmitBackend.Tests.Integration.Utils.Logging;

public partial class ProblemDetailsTraceIdTests
{
    [Fact]
    public async Task Post_WhenTraceHeaderIsAbsent_ReturnsActivityTraceId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new TestApplicationFactory();
        using var client = factory.CreateAuthenticatedClient();

        var traceId = await PostBadRequest(client, cancellationToken);

        Assert.Matches(ActivityTraceIdPattern(), traceId);
    }

    [Fact]
    public async Task Post_WhenTraceHeaderIsPresent_ReturnsHeader()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new TestApplicationFactory();
        using var client = factory.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("x-cdp-request-id", "trace-mismatch-check-001");

        var traceId = await PostBadRequest(client, cancellationToken);

        Assert.Equal("trace-mismatch-check-001", traceId);
    }

    [Fact]
    public async Task Post_WhenTraceHeaderIsBlank_ReturnsActivityTraceId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new TestApplicationFactory();
        using var client = factory.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("x-cdp-request-id", "   ");

        var traceId = await PostBadRequest(client, cancellationToken);

        Assert.Matches(ActivityTraceIdPattern(), traceId);
    }

    private static async Task<string> PostBadRequest(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync(
            "/pom-submissions",
            new StringContent("""{"regulatorNote":"ignore me"}""", Encoding.UTF8, "application/json"),
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var traceId = document.RootElement.GetProperty("traceId").GetString();
        Assert.NotNull(traceId);
        return traceId;
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex ActivityTraceIdPattern();

    private sealed class TestApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Production);
            builder.UseSetting("Jwt:MetadataAddress", "https://idp.test/.well-known/openid-configuration");
            builder.UseSetting("Jwt:Audience", "test-audience");
            builder.ConfigureTestAuthentication();
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IExampleRepository>();
                services.AddSingleton(Substitute.For<IExampleRepository>());
            });
        }
    }
}
