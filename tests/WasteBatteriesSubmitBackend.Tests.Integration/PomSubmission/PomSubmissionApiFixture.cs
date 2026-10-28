using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using WasteBatteriesSubmitBackend.Config;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Services;
using WasteBatteriesSubmitBackend.Tests.Common;
using WasteBatteriesSubmitBackend.Tests.Integration.Utils;

namespace WasteBatteriesSubmitBackend.Tests.Integration.PomSubmission;

public sealed class PomSubmissionApiFixture : MongoApiFixture
{
    public const string SchemeOrgId = "674a1b2c3d4e5f6789012345";
    public const string MemberOrgId = "674a1b2c3d4e5f6789012346";

    public static readonly DateTime CreatedAt = new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);

    public PomSubmissionApiFixture()
        : base("pom_it")
    {
    }

    public CapturingLogger<PomSubmissionService> Logger { get; } = new();

    public PomSubmissionConfig Defaults => Factory.Services.GetRequiredService<IOptions<PomSubmissionConfig>>().Value;

    public IServiceProvider Services => Factory.Services;

    public IMongoCollection<BsonDocument> Submissions => Collection(PomSubmissionRepository.CollectionName);

    protected override DateTime? FixedUtcNow => CreatedAt;

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<ILogger<PomSubmissionService>>();
        services.AddSingleton<ILogger<PomSubmissionService>>(Logger);
    }

    protected override void OnStarted()
    {
        _ = Factory.Services.GetRequiredService<IPomSubmissionRepository>();
    }
}
