using FluentValidation;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using WasteBatteriesSubmitBackend.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Queries;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Services;
using WasteBatteriesSubmitBackend.Tests.Common;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Services;

public partial class PomSubmissionServiceTests
{
    private static readonly int[] s_trimmedCompliancePeriodYear = [SamplePomSubmission.CompliancePeriodYear];

    private static readonly string[] s_trimmedCompliancePeriodQuarter = [SamplePomSubmission.CompliancePeriodQuarter];

    [Fact]
    public async Task CreateAsync_WhenRequestIsValid_SavesSubmissionAndLogsIt()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        ReturnNoSubmissions(repository);
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        var request = ValidRequest();

        // Act
        var submission = await service.CreateAsync(request, TestContext.Current.CancellationToken);

        // Assert
        await repository.Received(1).FindAsync(
            Arg.Is<PomSubmissionQuery>(query =>
                query.SchemeOrgId == request.SchemeOrgId
                && query.MemberOrgId == request.MemberOrgId
                && query.CompliancePeriodYears != null
                && query.CompliancePeriodYears.SequenceEqual(new[] { request.CompliancePeriodYear })
                && query.CompliancePeriodQuarters != null
                && query.CompliancePeriodQuarters.SequenceEqual(new[] { request.CompliancePeriodQuarter })
                && query.IsCurrent == true),
            new PageRequest { PageSize = MaxPageSize },
            Arg.Any<CancellationToken>());
        await repository.Received(1).CreateAsync(
            Arg.Is<PomSubmissionModel>(saved =>
                saved.Id == submission.Id
                && saved.Status == "Submitted"
                && saved.Version == 1
                && saved.SubmittedOnUtc == s_createdAt
                && saved.SubmittedOnUtc == saved.CreatedAtUtc
                && saved.MemberName == SamplePomSubmission.MemberName
                && saved.TonnageEntries[0].Activity == "Placed"
                && saved.IsCurrent
                && saved.CreatedAtUtc == s_createdAt),
            Arg.Any<CancellationToken>());
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(
            $"Created POM submission {submission.Id} for scheme {submission.SchemeOrgId} member {submission.MemberOrgId}",
            entry.Message);
    }

    [Fact]
    public async Task CreateAsync_WhenSaveFails_DoesNotLog()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        ReturnNoSubmissions(repository);
        repository
            .CreateAsync(Arg.Any<PomSubmissionModel>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database unavailable"));
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);

        // Act
        var act = () => service.CreateAsync(ValidRequest(), TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task CreateAsync_WhenTonnageEntriesAreEmpty_ThrowsValidationException()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        ReturnNoSubmissions(repository);
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        var request = ValidRequest() with { TonnageEntries = [] };

        // Act
        var act = () => service.CreateAsync(request, TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<ValidationException>(act);
        await repository.Received(1).FindAsync(
            Arg.Any<PomSubmissionQuery>(),
            Arg.Any<PageRequest>(),
            Arg.Any<CancellationToken>());
        await repository.DidNotReceive().CreateAsync(Arg.Any<PomSubmissionModel>(), Arg.Any<CancellationToken>());
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task CreateAsync_WhenStringsHaveSurroundingWhitespace_SavesTheTrimmedSubmission()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        ReturnNoSubmissions(repository);
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        var request = ValidRequest() with
        {
            SchemeOrgId = $"  {SamplePomSubmission.SchemeOrgId}  ",
            MemberOrgId = $"  {SamplePomSubmission.MemberOrgId}  ",
            MemberName = $"  {SamplePomSubmission.MemberName}  ",
            MemberBprn = $"  {SamplePomSubmission.MemberBprn}  ",
            SchemeMembershipRef = $"  {SamplePomSubmission.SchemeMembershipRef}  ",
            CompliancePeriodQuarter = $"  {SamplePomSubmission.CompliancePeriodQuarter}  ",
            DataBasis = $"  {SamplePomSubmission.DataBasis}  ",
            TonnageEntries =
            [
                new TonnageEntryBase
                {
                    Classification = new BatteryClassification
                    {
                        BatteryCategory = "Industrial",
                        Chemistry = $"  {SamplePomSubmission.Chemistry}  "
                    },
                    Tonnes = $"  {SamplePomSubmission.Tonnes}  "
                }
            ]
        };

        // Act
        await service.CreateAsync(request, TestContext.Current.CancellationToken);

        // Assert
        await repository.Received(1).FindAsync(
            Arg.Is<PomSubmissionQuery>(query =>
                query.SchemeOrgId == SamplePomSubmission.SchemeOrgId
                && query.MemberOrgId == SamplePomSubmission.MemberOrgId
                && query.CompliancePeriodYears != null
                && query.CompliancePeriodYears.SequenceEqual(s_trimmedCompliancePeriodYear)
                && query.CompliancePeriodQuarters != null
                && query.CompliancePeriodQuarters.SequenceEqual(s_trimmedCompliancePeriodQuarter)
                && query.IsCurrent == true),
            new PageRequest { PageSize = MaxPageSize },
            Arg.Any<CancellationToken>());
        await repository.Received(1).CreateAsync(
            Arg.Is<PomSubmissionModel>(saved =>
                saved.MemberName == SamplePomSubmission.MemberName
                && saved.MemberBprn == SamplePomSubmission.MemberBprn
                && saved.SchemeMembershipRef == SamplePomSubmission.SchemeMembershipRef
                && saved.DataBasis == SamplePomSubmission.DataBasis
                && saved.TonnageEntries[0].Tonnes == SamplePomSubmission.Tonnes
                && saved.TonnageEntries[0].Classification.BatteryCategory == BatteryClassification.Portable
                && saved.TonnageEntries[0].Classification.Chemistry == SamplePomSubmission.Chemistry),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("  not-an-id  ", SamplePomSubmission.MemberOrgId, SamplePomSubmission.CompliancePeriodYear, SamplePomSubmission.CompliancePeriodQuarter)]
    [InlineData(SamplePomSubmission.SchemeOrgId, "not-an-id", SamplePomSubmission.CompliancePeriodYear, SamplePomSubmission.CompliancePeriodQuarter)]
    [InlineData(SamplePomSubmission.SchemeOrgId, SamplePomSubmission.MemberOrgId, 0, SamplePomSubmission.CompliancePeriodQuarter)]
    [InlineData(SamplePomSubmission.SchemeOrgId, SamplePomSubmission.MemberOrgId, SamplePomSubmission.CompliancePeriodYear, "   ")]
    public async Task CreateAsync_WhenCurrentSubmissionKeyIsNotWellFormed_DoesNotLookItUp(
        string schemeOrgId,
        string memberOrgId,
        int year,
        string quarter)
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        var request = ValidRequest() with
        {
            SchemeOrgId = schemeOrgId,
            MemberOrgId = memberOrgId,
            CompliancePeriodYear = year,
            CompliancePeriodQuarter = quarter
        };

        // Act
        var act = () => service.CreateAsync(request, TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<ValidationException>(act);
        await repository.DidNotReceive().FindAsync(
            Arg.Any<PomSubmissionQuery>(),
            Arg.Any<PageRequest>(),
            Arg.Any<CancellationToken>());
        await repository.DidNotReceive().CreateAsync(Arg.Any<PomSubmissionModel>(), Arg.Any<CancellationToken>());
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task CreateAsync_WhenChemistryDiffersOnlyByBlankText_ThrowsValidationException()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        ReturnNoSubmissions(repository);
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        var request = ValidRequest() with
        {
            TonnageEntries =
            [
                new TonnageEntryBase
                {
                    Classification = new BatteryClassification
                    {
                        Chemistry = $"  {SamplePomSubmission.Chemistry}  "
                    },
                    Tonnes = "1.000"
                },
                new TonnageEntryBase
                {
                    Classification = new BatteryClassification
                    {
                        Chemistry = SamplePomSubmission.Chemistry
                    },
                    Tonnes = "2.000"
                }
            ]
        };

        // Act
        var act = () => service.CreateAsync(request, TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(act);
        Assert.Contains(
            exception.Errors,
            error => error.PropertyName == "TonnageEntries"
                && error.ErrorMessage == "Each classification can only be reported once in a submission.");
        await repository.DidNotReceive().CreateAsync(Arg.Any<PomSubmissionModel>(), Arg.Any<CancellationToken>());
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task CreateAsync_WhenCurrentSlotIsTakenDuringSave_ThrowsValidationException()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        ReturnNoSubmissions(repository);
        repository
            .CreateAsync(Arg.Any<PomSubmissionModel>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DuplicateKeyOnSaveException(new InvalidOperationException("duplicate key")));
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);

        // Act
        var act = () => service.CreateAsync(ValidRequest(), TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(act);
        var error = Assert.Single(exception.Errors);
        Assert.Equal("Submission", error.PropertyName);
        Assert.Equal(
            "A current submission already exists for this scheme, member, year and quarter.",
            error.ErrorMessage);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task CreateAsync_WhenModelIsInvalidAndCurrentSubmissionExists_ReturnsAllErrors()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        repository
            .FindAsync(Arg.Any<PomSubmissionQuery>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaginatedList<PomSubmissionModel>(
                [new PomSubmissionModel { Id = SamplePomSubmission.SchemeOrgId, CreatedAtUtc = s_createdAt }],
                1,
                1,
                1));
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        var request = ValidRequest() with { MemberName = "" };

        // Act
        var act = () => service.CreateAsync(request, TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(act);
        Assert.Contains(exception.Errors, error => error.PropertyName == "MemberName");
        Assert.Contains(
            exception.Errors,
            error => error.PropertyName == "Submission"
                && error.ErrorMessage == "A current submission already exists for this scheme, member, year and quarter.");
        await repository.DidNotReceive().CreateAsync(Arg.Any<PomSubmissionModel>(), Arg.Any<CancellationToken>());
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task CreateAsync_WhenCurrentSubmissionExists_ThrowsValidationException()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        repository
            .FindAsync(Arg.Any<PomSubmissionQuery>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaginatedList<PomSubmissionModel>(
                [new PomSubmissionModel { Id = SamplePomSubmission.SchemeOrgId, CreatedAtUtc = s_createdAt }],
                1,
                1,
                1));
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);

        // Act
        var act = () => service.CreateAsync(ValidRequest(), TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(act);
        Assert.Contains(
            exception.Errors,
            error => error.PropertyName == "Submission"
                && error.ErrorMessage == "A current submission already exists for this scheme, member, year and quarter.");
        await repository.DidNotReceive().CreateAsync(Arg.Any<PomSubmissionModel>(), Arg.Any<CancellationToken>());
        Assert.Empty(logger.Entries);
    }
}
