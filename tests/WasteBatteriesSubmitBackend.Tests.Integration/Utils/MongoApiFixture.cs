using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Bson;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using WasteBatteriesSubmitBackend.Tests.Common;
using WasteBatteriesSubmitBackend.Utils.Mongo;
using WasteBatteriesSubmitBackend.Utils.Time;

namespace WasteBatteriesSubmitBackend.Tests.Integration.Utils;

// Host, authentication and MongoDB shared by endpoint tests. A feature fixture
// adds its own clock, logger and collection on top.

//Testcontainers.MongoDb - see https://devblogs.microsoft.com/ise/testing-with-testcontainers/ and https://dotnet.testcontainers.org/modules/mongodb/
public abstract class MongoApiFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:7").Build();
    private WebApplicationFactory<Program>? _factory;

    protected MongoApiFixture(string databasePrefix)
    {
        DatabaseName = $"{databasePrefix}_{Guid.NewGuid():N}";
    }

    protected WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public string DatabaseName { get; }

    protected virtual DateTime? FixedUtcNow => null;

    public HttpClient CreateClient() => Factory.CreateAuthenticatedClient();

    public HttpClient CreateAnonymousClient() => Factory.CreateClient();

    protected IMongoCollection<BsonDocument> Collection(string name) =>
        Factory.Services.GetRequiredService<IMongoDbClientFactory>().GetCollection<BsonDocument>(name);

    protected abstract void ConfigureTestServices(IServiceCollection services);

    protected virtual void OnStarted()
    {
    }

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        _factory = new ApplicationFactory(
            _container.GetConnectionString(),
            DatabaseName,
            FixedUtcNow,
            ConfigureTestServices);
        Factory = _factory;
        OnStarted();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_factory is not null)
            {
                await _factory.DisposeAsync();
            }

            await _container.DisposeAsync();
        }
        finally
        {
            GC.SuppressFinalize(this);
        }
    }

    private sealed class ApplicationFactory(
        string connectionString,
        string databaseName,
        DateTime? fixedUtcNow,
        Action<IServiceCollection> configureServices) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestAuthentication();
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Mongo:DatabaseUri"] = connectionString,
                    ["Mongo:DatabaseName"] = databaseName
                });
            });
            builder.ConfigureServices(services =>
            {
                if (fixedUtcNow is not null)
                {
                    services.RemoveAll<ITimeProvider>();
                    services.AddSingleton<ITimeProvider>(new FixedTime(fixedUtcNow.Value));
                }

                configureServices(services);
            });
        }
    }
}
