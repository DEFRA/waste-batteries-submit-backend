using Microsoft.Extensions.Options;
using NSubstitute;
using WasteBatteriesSubmitBackend.Config;
using WasteBatteriesSubmitBackend.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Mapping;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Queries;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Services;
using WasteBatteriesSubmitBackend.PomSubmission.Validators;
using WasteBatteriesSubmitBackend.Tests.Unit.Utils.Logging;
using WasteBatteriesSubmitBackend.Tests.Unit.Utils.Time;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Services;

public partial class PomSubmissionServiceTests
{
    private const int MaxPageSize = 25;

    private const int MaxGzipPageSize = 40;

    private static readonly DateTime CreatedAt = new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);

    private static void ReturnNoSubmissions(IPomSubmissionRepository repository) =>
        repository
            .FindAsync(Arg.Any<PomSubmissionQuery>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaginatedList<PomSubmissionModel>([], 0, 1, 0));

    private static PomSubmissionService Service(IPomSubmissionRepository repository, CapturingLogger<PomSubmissionService> logger) =>
        new(
            repository,
            new PomSubmissionMapper(Options.Create(new PomSubmissionConfig
            {
                InitialStatus = "Submitted",
                InitialVersion = 1,
                InitialActivity = "Placed",
                MaxPageSize = MaxPageSize,
                MaxGzipPageSize = MaxGzipPageSize
            })),
            Options.Create(new PomSubmissionConfig
            {
                InitialStatus = "Submitted",
                InitialVersion = 1,
                InitialActivity = "Placed",
                MaxPageSize = MaxPageSize,
                MaxGzipPageSize = MaxGzipPageSize
            }),
            new PomSubmissionRequestValidator(),
            new PomSubmissionListRequestValidator(),
            new FixedTime(CreatedAt),
            logger);

    private static PomSubmissionRequest ValidRequest() => new()
    {
        SchemeOrgId = "674a1b2c3d4e5f6789012345",
        MemberOrgId = "674a1b2c3d4e5f6789012346",
        MemberName = "Northwind Batteries Ltd",
        MemberBprn = "BPRN-EA-2026-000123",
        SchemeMembershipRef = "SCH-00417",
        CompliancePeriodYear = 2026,
        CompliancePeriodQuarter = "Quarter 2",
        DataBasis = "Actual",
        TonnageEntries =
        [
            new TonnageEntryBase
            {
                Classification = new BatteryClassification
                {
                    BatteryCategory = "Portable",
                    Chemistry = "Alkaline"
                },
                Tonnes = "12.500"
            }
        ]
    };
}
