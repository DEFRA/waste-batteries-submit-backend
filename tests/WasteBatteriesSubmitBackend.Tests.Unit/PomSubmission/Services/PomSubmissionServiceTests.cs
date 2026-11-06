using Microsoft.Extensions.Options;
using NSubstitute;
using WasteBatteriesSubmitBackend.Config;
using WasteBatteriesSubmitBackend.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Mapping;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Filters;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Services;
using WasteBatteriesSubmitBackend.PomSubmission.Validators;
using WasteBatteriesSubmitBackend.Tests.Common;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Services;

public partial class PomSubmissionServiceTests
{
    private const int MaxPageSize = 25;

    private const int MaxGzipPageSize = 40;

    private static readonly DateTime s_createdAt = SamplePomSubmission.CreatedAt;

    private static void ReturnNoSubmissions(IPomSubmissionRepository repository) =>
        repository
            .FindAsync(Arg.Any<PomSubmissionFilter>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaginatedList<PomSubmissionModel>([], 0, 1, 0));

    private static readonly IOptions<PomSubmissionConfig> s_config = Options.Create(new PomSubmissionConfig
    {
        InitialStatus = "Submitted",
        InitialVersion = 1,
        InitialActivity = "Placed",
        InitialBatteryCategory = "Portable",
        MaxPageSize = MaxPageSize,
        MaxGzipPageSize = MaxGzipPageSize
    });

    private static PomSubmissionService Service(
        IPomSubmissionRepository repository,
        CapturingLogger<PomSubmissionService> logger,
        IUnitOfWork? unitOfWork = null) =>
        new(
            repository,
            unitOfWork ?? Substitute.For<IUnitOfWork>(),
            new PomSubmissionMapper(s_config),
            s_config,
            new PomSubmissionValidators(
                new PomSubmissionRequestValidator(),
                new PomSubmissionFilterRequestValidator()),
            new FixedTime(s_createdAt),
            logger);

    private static PomSubmissionRequest ValidRequest() => SamplePomSubmission.Request();
}
