using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using WasteBatteriesSubmitBackend.Example.Repositories;
using WasteBatteriesSubmitBackend.Tests.Integration.Utils;

namespace WasteBatteriesSubmitBackend.Tests.Integration.Utils.Logging;

public class ExceptionLoggingHandlerTests
{
    [Fact]
    public async Task Get_WhenEndpointThrows_ReturnsProblemDetailsWithoutExceptionMessage()
    {
        // Arrange
        const string exceptionMessage = "secret boom";
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new TestApplicationFactory();
        using var client = factory.CreateAuthenticatedClient();
        factory.MockPersistence
            .GetAllAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException(exceptionMessage));

        // Act
        var response = await client.GetAsync("/db-demo", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var problem = JsonSerializer.Deserialize<ProblemDetails>(body);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
        Assert.Equal("An error occurred while processing your request.", problem.Title);
        Assert.DoesNotContain(exceptionMessage, body);
    }

    [Fact]
    public async Task Post_WhenBodyDoesNotBindInProduction_ReturnsBadRequestProblemDetails()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new TestApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Production);
            // Production has no Jwt section, and Program reads it before test configuration is applied.
            builder.UseSetting("Jwt:MetadataAddress", "https://idp.test/.well-known/openid-configuration");
            builder.UseSetting("Jwt:Audience", "test-audience");
        });
        using var client = factory.CreateAuthenticatedClient();

        // Act
        var response = await client.PostAsync(
            "/pom-submissions",
            new StringContent("""{"regulatorNote":"ignore me"}""", Encoding.UTF8, "application/json"),
            cancellationToken);
        var problem = JsonSerializer.Deserialize<ProblemDetails>(
            await response.Content.ReadAsStringAsync(cancellationToken),
            JsonSerializerOptions.Web);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal("Bad request", problem.Title);
        Assert.Contains("JSON", problem.Detail);
    }

    private sealed class TestApplicationFactory : WebApplicationFactory<Program>
    {
        public readonly IExampleRepository MockPersistence = Substitute.For<IExampleRepository>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestAuthentication();

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IExampleRepository>();
                services.AddSingleton(MockPersistence);
            });
        }
    }
}
