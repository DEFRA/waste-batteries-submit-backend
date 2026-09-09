using System.Net;
using System.Net.Http.Json;
using WasteBatteriesSubmitBackend.ExampleData.Models;
using WasteBatteriesSubmitBackend.ExampleData.Services;
using WasteBatteriesSubmitBackend.Test.Utils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace WasteBatteriesSubmitBackend.Test.ExampleData.Endpoints;

public class ExampleDataEndpointsTest
{
    [Fact]
    public async Task Post_saves_example_text_with_the_user_id()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new TestApplicationFactory();
        using var client = factory.CreateAuthenticatedClient();

        var saved = new ExampleDataModel
        {
            Id = "example-1",
            ExampleText = "Hello backend",
            UserId = "user-123",
            CreatedAt = new DateTime(2026, 8, 22, 13, 0, 0, DateTimeKind.Utc)
        };

        factory.MockPersistence
            .SaveAsync(Arg.Is("Hello backend"), Arg.Is(TestAuthentication.UserId), Arg.Any<CancellationToken>())
            .Returns(saved);

        var response = await client.PostAsJsonAsync("/example", new CreateExampleDataRequest
        {
            ExampleText = "Hello backend"
        }, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/example/example-1", response.Headers.Location?.AbsolutePath);

        var body = await response.Content.ReadFromJsonAsync<ExampleDataModel>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal("example-1", body.Id);
        Assert.Equal("Hello backend", body.ExampleText);
        Assert.Equal("user-123", body.UserId);
        Assert.Equal(saved.CreatedAt, body.CreatedAt);
    }

    [Fact]
    public async Task Post_trims_example_text_and_user_id_before_saving()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new TestApplicationFactory();
        using var client = factory.CreateAuthenticatedClient();

        factory.MockPersistence
            .SaveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ExampleDataModel
            {
                ExampleText = "Hello backend",
                UserId = "user-123"
            });

        var response = await client.PostAsJsonAsync("/example", new CreateExampleDataRequest
        {
            ExampleText = "  Hello backend  "
        }, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await factory.MockPersistence.Received()
            .SaveAsync(Arg.Is("Hello backend"), Arg.Is("user-123"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Post_rejects_empty_example_text()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new TestApplicationFactory();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/example", new CreateExampleDataRequest
        {
            ExampleText = string.Empty
        }, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);
        Assert.NotNull(problem);
        Assert.Contains(nameof(CreateExampleDataRequest.ExampleText), problem.Errors.Keys);
    }

    [Fact]
    public async Task Post_rejects_whitespace_only_example_text()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new TestApplicationFactory();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/example", new CreateExampleDataRequest
        {
            ExampleText = "   "
        }, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);
        Assert.NotNull(problem);
        Assert.Contains(nameof(CreateExampleDataRequest.ExampleText), problem.Errors.Keys);
    }

    [Fact]
    public async Task Post_rejects_requests_without_a_bearer_token()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/example", new CreateExampleDataRequest
        {
            ExampleText = "Hello backend"
        }, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_filters_by_the_authenticated_user_id()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new TestApplicationFactory();
        using var client = factory.CreateAuthenticatedClient();

        factory.MockPersistence
            .GetAllAsync(Arg.Is(TestAuthentication.UserId), Arg.Any<CancellationToken>())
            .Returns([
                new ExampleDataModel
                {
                    Id = "example-1",
                    ExampleText = "First",
                    UserId = "user-123",
                    CreatedAt = new DateTime(2026, 8, 22, 13, 0, 0, DateTimeKind.Utc)
                }
            ]);

        var result = await client.GetFromJsonAsync<List<ExampleDataModel>>("/example", cancellationToken);

        await factory.MockPersistence.Received().GetAllAsync(Arg.Is(TestAuthentication.UserId), Arg.Any<CancellationToken>());
        Assert.NotNull(result);
        var example = Assert.Single(result);
        Assert.Equal("First", example.ExampleText);
        Assert.Equal("user-123", example.UserId);
    }

    [Fact]
    public async Task Get_ignores_query_user_id_and_uses_authenticated_user_id()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new TestApplicationFactory();
        using var client = factory.CreateAuthenticatedClient();

        factory.MockPersistence
            .GetAllAsync(Arg.Is(TestAuthentication.UserId), Arg.Any<CancellationToken>())
            .Returns([]);

        var response = await client.GetAsync("/example?userId=attacker", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await factory.MockPersistence.Received().GetAllAsync(Arg.Is(TestAuthentication.UserId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_rejects_requests_without_a_bearer_token()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/example", cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_by_id_returns_the_matching_example()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new TestApplicationFactory();
        using var client = factory.CreateAuthenticatedClient();

        factory.MockPersistence
            .GetByIdAsync(Arg.Is("example-1"), Arg.Any<CancellationToken>())
            .Returns(new ExampleDataModel
            {
                Id = "example-1",
                ExampleText = "First",
                UserId = TestAuthentication.UserId
            });

        var result = await client.GetFromJsonAsync<ExampleDataModel>("/example/example-1", cancellationToken);

        Assert.NotNull(result);
        Assert.Equal("example-1", result.Id);
    }

    [Fact]
    public async Task Get_by_id_returns_not_found_for_unknown_example()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new TestApplicationFactory();
        using var client = factory.CreateAuthenticatedClient();

        factory.MockPersistence
            .GetByIdAsync(Arg.Is("missing"), Arg.Any<CancellationToken>())
            .Returns((ExampleDataModel?)null);

        var response = await client.GetAsync("/example/missing", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_by_id_returns_not_found_for_another_users_example()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new TestApplicationFactory();
        using var client = factory.CreateAuthenticatedClient();

        factory.MockPersistence
            .GetByIdAsync(Arg.Is("example-1"), Arg.Any<CancellationToken>())
            .Returns(new ExampleDataModel
            {
                Id = "example-1",
                ExampleText = "First",
                UserId = "another-user"
            });

        var response = await client.GetAsync("/example/example-1", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class TestApplicationFactory : WebApplicationFactory<Program>
    {
        public readonly IExampleDataPersistence MockPersistence = Substitute.For<IExampleDataPersistence>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestAuthentication();

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IExampleDataPersistence>();
                services.AddSingleton(MockPersistence);
            });
        }
    }
}
