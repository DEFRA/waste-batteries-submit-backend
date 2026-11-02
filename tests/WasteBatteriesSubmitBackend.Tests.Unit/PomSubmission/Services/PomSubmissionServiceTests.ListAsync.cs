using FluentValidation;
using NSubstitute;
using WasteBatteriesSubmitBackend.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Queries;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Services;
using WasteBatteriesSubmitBackend.Tests.Common;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Services;

public partial class PomSubmissionServiceTests
{
    private static readonly int[] s_combinedCompliancePeriodYears = [2024, 2025, SamplePomSubmission.CompliancePeriodYear];

    private static readonly int[] s_yearOnly = [SamplePomSubmission.CompliancePeriodYear];

    private static readonly string[] s_combinedCompliancePeriodQuarters = [SamplePomSubmission.CompliancePeriodQuarter, "Quarter 1"];

    private static readonly string[] s_quarterOnly = [SamplePomSubmission.CompliancePeriodQuarter];

    [Fact]
    public async Task GetAsync_WhenNoBusinessFilterIsSet_ThrowsValidationException()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);

        // Act
        var act = () => service.GetAsync(
            new PomSubmissionListRequest { IsCurrent = false },
            false,
            TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(act);
        var error = Assert.Single(exception.Errors);
        Assert.Equal("Filters", error.PropertyName);
        Assert.Equal(
            "At least one of schemeOrgId, memberOrgId, compliancePeriodYears or compliancePeriodQuarters is required.",
            error.ErrorMessage);
        await repository.DidNotReceive().FindAsync(
            Arg.Any<PomSubmissionQuery>(),
            Arg.Any<PageRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_WhenFiltersAreCombined_ReadsTheIntersectionAndPagesIt()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        repository
            .FindAsync(Arg.Any<PomSubmissionQuery>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaginatedList<PomSubmissionModel>([], 0, 1, 2));

        // Act
        await service.GetAsync(
            new PomSubmissionListRequest
            {
                SchemeOrgId = $"  {SamplePomSubmission.SchemeOrgId}  ",
                MemberOrgId = "674a1b2c3d4e5f6789012347",
                CompliancePeriodYears = [2024, 2025, SamplePomSubmission.CompliancePeriodYear],
                CompliancePeriodQuarters = [$"  {SamplePomSubmission.CompliancePeriodQuarter}  ", "Quarter 1"],
                PageSize = 2
            },
            false,
            TestContext.Current.CancellationToken);

        // Assert
        await repository.Received(1).FindAsync(
            Arg.Is<PomSubmissionQuery>(query =>
                query.Id == null
                && query.SchemeOrgId == SamplePomSubmission.SchemeOrgId
                && query.MemberOrgId == "674a1b2c3d4e5f6789012347"
                && query.IsCurrent == true
                && query.CompliancePeriodYears != null
                && query.CompliancePeriodYears.SequenceEqual(s_combinedCompliancePeriodYears)
                && query.CompliancePeriodQuarters != null
                && query.CompliancePeriodQuarters.SequenceEqual(s_combinedCompliancePeriodQuarters)),
            new PageRequest { PageSize = 2 },
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(SamplePomSubmission.SchemeOrgId, null)]
    [InlineData(null, "674a1b2c3d4e5f6789012347")]
    public async Task GetAsync_WhenOneOrganisationIsSet_DefaultsToCurrent(
        string? schemeOrgId,
        string? memberOrgId)
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        repository
            .FindAsync(Arg.Any<PomSubmissionQuery>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaginatedList<PomSubmissionModel>([], 0, 1, 0));

        // Act
        await service.GetAsync(
            new PomSubmissionListRequest
            {
                SchemeOrgId = schemeOrgId,
                MemberOrgId = memberOrgId
            },
            false,
            TestContext.Current.CancellationToken);

        // Assert
        await repository.Received(1).FindAsync(
            Arg.Is<PomSubmissionQuery>(query =>
                query.Id == null
                && query.SchemeOrgId == schemeOrgId
                && query.MemberOrgId == memberOrgId
                && query.CompliancePeriodYears == null
                && query.CompliancePeriodQuarters == null
                && query.IsCurrent == true),
            Arg.Any<PageRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_WhenAYearOrQuarterIsSet_DefaultsToCurrent()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        repository
            .FindAsync(Arg.Any<PomSubmissionQuery>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaginatedList<PomSubmissionModel>([], 0, 1, 0));

        // Act
        await service.GetAsync(
            new PomSubmissionListRequest
            {
                CompliancePeriodYears = [SamplePomSubmission.CompliancePeriodYear]
            },
            false,
            TestContext.Current.CancellationToken);
        await service.GetAsync(
            new PomSubmissionListRequest
            {
                CompliancePeriodQuarters = [SamplePomSubmission.CompliancePeriodQuarter]
            },
            false,
            TestContext.Current.CancellationToken);

        // Assert
        await repository.Received(1).FindAsync(
            Arg.Is<PomSubmissionQuery>(query =>
                query.Id == null
                && query.SchemeOrgId == null
                && query.MemberOrgId == null
                && query.CompliancePeriodYears != null
                && query.CompliancePeriodYears.SequenceEqual(s_yearOnly)
                && query.CompliancePeriodQuarters == null
                && query.IsCurrent == true),
            Arg.Any<PageRequest>(),
            Arg.Any<CancellationToken>());
        await repository.Received(1).FindAsync(
            Arg.Is<PomSubmissionQuery>(query =>
                query.Id == null
                && query.SchemeOrgId == null
                && query.MemberOrgId == null
                && query.CompliancePeriodYears == null
                && query.CompliancePeriodQuarters != null
                && query.CompliancePeriodQuarters.SequenceEqual(s_quarterOnly)
                && query.IsCurrent == true),
            Arg.Any<PageRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_WhenYearAndQuarterListsAreEmpty_DoesNotApplyThem()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        repository
            .FindAsync(Arg.Any<PomSubmissionQuery>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaginatedList<PomSubmissionModel>([], 0, 1, 0));

        // Act
        await service.GetAsync(
            new PomSubmissionListRequest
            {
                SchemeOrgId = SamplePomSubmission.SchemeOrgId,
                CompliancePeriodYears = [],
                CompliancePeriodQuarters = []
            },
            false,
            TestContext.Current.CancellationToken);

        // Assert
        await repository.Received(1).FindAsync(
            Arg.Is<PomSubmissionQuery>(query =>
                query.SchemeOrgId == SamplePomSubmission.SchemeOrgId
                && query.MemberOrgId == null
                && query.CompliancePeriodYears == null
                && query.CompliancePeriodQuarters == null
                && query.IsCurrent == true),
            Arg.Any<PageRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_WhenCurrentIsSupplied_KeepsThatValue()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        repository
            .FindAsync(Arg.Any<PomSubmissionQuery>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaginatedList<PomSubmissionModel>([], 0, 1, 0));

        // Act
        await service.GetAsync(
            new PomSubmissionListRequest
            {
                SchemeOrgId = SamplePomSubmission.SchemeOrgId,
                IsCurrent = false,
                PageNumber = 2,
                PageSize = 10
            },
            false,
            TestContext.Current.CancellationToken);

        // Assert
        await repository.Received(1).FindAsync(
            Arg.Is<PomSubmissionQuery>(query =>
                query.Id == null
                && query.SchemeOrgId == SamplePomSubmission.SchemeOrgId
                && query.MemberOrgId == null
                && query.CompliancePeriodYears == null
                && query.CompliancePeriodQuarters == null
                && query.IsCurrent == false),
            new PageRequest { PageNumber = 2, PageSize = 10 },
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_WhenTheListRequestIsInvalid_ThrowsValidationException()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        var request = new PomSubmissionListRequest
        {
            SchemeOrgId = "not-an-id",
            MemberOrgId = "not-an-id",
            CompliancePeriodYears = [12],
            CompliancePeriodQuarters = ["  ", null!],
            PageNumber = 0,
            PageSize = 0
        };

        // Act
        var act = () => service.GetAsync(request, false, TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(act);
        Assert.Contains(exception.Errors, error => error.PropertyName == "SchemeOrgId");
        Assert.Contains(exception.Errors, error => error.PropertyName == "MemberOrgId");
        Assert.Contains(exception.Errors, error => error.PropertyName == "CompliancePeriodYears[0]");
        Assert.Contains(exception.Errors, error => error.PropertyName.StartsWith("CompliancePeriodQuarters", StringComparison.Ordinal));
        Assert.Contains(exception.Errors, error => error.PropertyName == "PageNumber");
        Assert.Contains(exception.Errors, error => error.PropertyName == "PageSize");
        await repository.DidNotReceive().FindAsync(
            Arg.Any<PomSubmissionQuery>(),
            Arg.Any<PageRequest>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task GetAsync_WhenPageSizeIsOmitted_LimitsThePageToTheConfiguredMaximum()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        ReturnNoSubmissions(repository);

        // Act
        await service.GetAsync(
            new PomSubmissionListRequest { SchemeOrgId = SamplePomSubmission.SchemeOrgId },
            false,
            TestContext.Current.CancellationToken);

        // Assert
        await repository.Received(1).FindAsync(
            Arg.Any<PomSubmissionQuery>(),
            new PageRequest { PageSize = MaxPageSize },
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_WhenPageSizeExceedsTheConfiguredMaximum_LimitsThePage()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        ReturnNoSubmissions(repository);

        // Act
        await service.GetAsync(
            new PomSubmissionListRequest
            {
                SchemeOrgId = SamplePomSubmission.SchemeOrgId,
                PageNumber = 3,
                PageSize = MaxPageSize + 100
            },
            false,
            TestContext.Current.CancellationToken);

        // Assert
        await repository.Received(1).FindAsync(
            Arg.Any<PomSubmissionQuery>(),
            new PageRequest { PageNumber = 3, PageSize = MaxPageSize },
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_WhenPageNumberIsSetWithoutPageSize_LimitsThePageToTheConfiguredMaximum()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        ReturnNoSubmissions(repository);

        // Act
        await service.GetAsync(
            new PomSubmissionListRequest
            {
                SchemeOrgId = SamplePomSubmission.SchemeOrgId,
                PageNumber = 2
            },
            false,
            TestContext.Current.CancellationToken);

        // Assert
        await repository.Received(1).FindAsync(
            Arg.Any<PomSubmissionQuery>(),
            new PageRequest { PageNumber = 2, PageSize = MaxPageSize },
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_WhenGzipIsAcceptedAndPageSizeIsOmitted_UsesTheLargerLimit()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        ReturnNoSubmissions(repository);

        // Act
        await service.GetAsync(
            new PomSubmissionListRequest { SchemeOrgId = SamplePomSubmission.SchemeOrgId },
            gzipResponse: true,
            TestContext.Current.CancellationToken);

        // Assert
        await repository.Received(1).FindAsync(
            Arg.Any<PomSubmissionQuery>(),
            new PageRequest { PageSize = MaxGzipPageSize },
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_WhenGzipIsAccepted_KeepsAPageSizeBetweenTheTwoLimits()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        ReturnNoSubmissions(repository);

        // Act
        await service.GetAsync(
            new PomSubmissionListRequest
            {
                SchemeOrgId = SamplePomSubmission.SchemeOrgId,
                PageSize = MaxPageSize + 5
            },
            gzipResponse: true,
            TestContext.Current.CancellationToken);
        await service.GetAsync(
            new PomSubmissionListRequest
            {
                SchemeOrgId = SamplePomSubmission.SchemeOrgId,
                PageSize = MaxGzipPageSize + 5
            },
            gzipResponse: true,
            TestContext.Current.CancellationToken);

        // Assert
        await repository.Received(1).FindAsync(
            Arg.Any<PomSubmissionQuery>(),
            new PageRequest { PageSize = MaxPageSize + 5 },
            Arg.Any<CancellationToken>());
        await repository.Received(1).FindAsync(
            Arg.Any<PomSubmissionQuery>(),
            new PageRequest { PageSize = MaxGzipPageSize },
            Arg.Any<CancellationToken>());
    }
}
