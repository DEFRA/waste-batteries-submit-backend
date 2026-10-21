using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using WasteBatteriesSubmitBackend.Config;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Services;
using WasteBatteriesSubmitBackend.Tests.Integration.Utils;
using WasteBatteriesSubmitBackend.Utils.Mongo;
using WasteBatteriesSubmitBackend.Utils.Time;

namespace WasteBatteriesSubmitBackend.Tests.Integration.PomSubmission;

public sealed class PomSubmissionApiFixture : IAsyncLifetime
{
    public const string SchemeOrgId = "674a1b2c3d4e5f6789012345";
    public const string MemberOrgId = "674a1b2c3d4e5f6789012346";

    public static readonly DateTime CreatedAt = new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);

    private readonly MongoDbContainer container = new MongoDbBuilder("mongo:7").Build();
    private WebApplicationFactory<Program>? factory;

    public CapturingLogger Logger { get; } = new();

    public string DatabaseName { get; } = $"pom_it_{Guid.NewGuid():N}";

    public HttpClient CreateClient() => factory!.CreateAuthenticatedClient();

    public HttpClient CreateAnonymousClient() => factory!.CreateClient();

    public PomSubmissionConfig Defaults => factory!.Services.GetRequiredService<IOptions<PomSubmissionConfig>>().Value;

    public IMongoCollection<BsonDocument> Submissions
        => factory!.Services.GetRequiredService<IMongoDbClientFactory>().GetCollection<BsonDocument>(PomSubmissionRepository.CollectionName);

    public async ValueTask InitializeAsync()
    {
        await container.StartAsync();
        factory = new PomSubmissionApplicationFactory(container.GetConnectionString(), DatabaseName, Logger);
        _ = factory.Services.GetRequiredService<IPomSubmissionRepository>();
    }

    public async ValueTask DisposeAsync()
    {
        if (factory is not null)
        {
            await factory.DisposeAsync();
        }

        await container.DisposeAsync();
    }

    private sealed class PomSubmissionApplicationFactory(
        string connectionString,
        string databaseName,
        CapturingLogger logger) : WebApplicationFactory<Program>
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
                services.RemoveAll<ITimeProvider>();
                services.AddSingleton<ITimeProvider>(new FixedTime(CreatedAt));
                services.RemoveAll<ILogger<PomSubmissionService>>();
                services.AddSingleton<ILogger<PomSubmissionService>>(logger);
            });
        }
    }

    private sealed class FixedTime(DateTime utcNow) : ITimeProvider
    {
        public DateTime UtcNow => utcNow;
    }
}

public sealed class CapturingLogger : ILogger<PomSubmissionService>
{
    public List<LogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception)));

    public sealed record LogEntry(LogLevel Level, EventId EventId, string Message);
}
