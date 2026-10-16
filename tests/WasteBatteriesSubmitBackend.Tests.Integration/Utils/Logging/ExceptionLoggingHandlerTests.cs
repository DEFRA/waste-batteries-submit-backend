using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using WasteBatteriesSubmitBackend.Example.Repositories;
using WasteBatteriesSubmitBackend.Tests.Integration.Utils;

namespace WasteBatteriesSubmitBackend.Tests.Integration.Utils.Logging;

public class ExceptionLoggingHandlerTests
{
    [Fact]
    public async Task Unhandled_endpoint_exception_returns_problem_details_without_the_exception_message()
    {
        const string exceptionMessage = "secret boom";
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new TestApplicationFactory();
        using var client = factory.CreateAuthenticatedClient();

        factory.MockPersistence
            .GetAllAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException(exceptionMessage));

        var response = await client.GetAsync("/db-demo", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var problem = JsonSerializer.Deserialize<ProblemDetails>(body);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
        Assert.Equal("An error occurred while processing your request.", problem.Title);
        Assert.DoesNotContain(exceptionMessage, body);
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
