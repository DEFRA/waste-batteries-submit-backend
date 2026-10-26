using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using WasteBatteriesSubmitBackend.Config;
using WasteBatteriesSubmitBackend.PomSubmission.Mapping;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Services;
using WasteBatteriesSubmitBackend.PomSubmission.Validators;
using WasteBatteriesSubmitBackend.Tests.Unit.Utils.Logging;
using WasteBatteriesSubmitBackend.Utils.Mongo;
using WasteBatteriesSubmitBackend.Tests.Unit.Utils.Time;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Services;

public class PomSubmissionServiceTests
{
    private static readonly DateTime CreatedAt = new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CreateAsync_WhenRequestIsValid_SavesSubmissionAndLogsIt()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        var request = ValidRequest();

        // Act
        var submission = await service.CreateAsync(request, TestContext.Current.CancellationToken);

        // Assert
        await repository.Received(1).FindCurrentAsync(
            request.SchemeOrgId,
            request.MemberOrgId,
            request.CompliancePeriodYear,
            request.CompliancePeriodQuarter,
            Arg.Any<CancellationToken>());
        await repository.Received(1).CreateAsync(
            Arg.Is<PomSubmissionModel>(saved =>
                saved.Id == submission.Id
                && saved.Status == "Submitted"
                && saved.Version == 1
                && saved.SubmittedOnUtc == CreatedAt
                && saved.SubmittedOnUtc == saved.CreatedAtUtc
                && saved.MemberName == "Northwind Batteries Ltd"
                && saved.TonnageEntries[0].Activity == "Placed"
                && saved.IsCurrent
                && saved.CreatedAtUtc == CreatedAt),
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
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        var request = ValidRequest() with { TonnageEntries = [] };

        // Act
        var act = () => service.CreateAsync(request, TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<ValidationException>(act);
        await repository.Received(1).FindCurrentAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await repository.DidNotReceive().CreateAsync(Arg.Any<PomSubmissionModel>(), Arg.Any<CancellationToken>());
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task CreateAsync_WhenStringsHaveSurroundingWhitespace_SavesTheTrimmedSubmission()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        var request = ValidRequest() with
        {
            SchemeOrgId = "  674a1b2c3d4e5f6789012345  ",
            MemberOrgId = "  674a1b2c3d4e5f6789012346  ",
            MemberName = "  Northwind Batteries Ltd  ",
            MemberBprn = "  BPRN-EA-2026-000123  ",
            SchemeMembershipRef = "  SCH-00417  ",
            CompliancePeriodQuarter = "  Quarter 2  ",
            DataBasis = "  Actual  ",
            TonnageEntries =
            [
                new TonnageEntryBase
                {
                    Classification = new BatteryClassification
                    {
                        BatteryCategory = "Industrial",
                        Chemistry = "  Alkaline  "
                    },
                    Tonnes = "  12.500  "
                }
            ]
        };

        // Act
        await service.CreateAsync(request, TestContext.Current.CancellationToken);

        // Assert
        await repository.Received(1).FindCurrentAsync(
            "674a1b2c3d4e5f6789012345",
            "674a1b2c3d4e5f6789012346",
            2026,
            "Quarter 2",
            Arg.Any<CancellationToken>());
        await repository.Received(1).CreateAsync(
            Arg.Is<PomSubmissionModel>(saved =>
                saved.MemberName == "Northwind Batteries Ltd"
                && saved.MemberBprn == "BPRN-EA-2026-000123"
                && saved.SchemeMembershipRef == "SCH-00417"
                && saved.DataBasis == "Actual"
                && saved.TonnageEntries[0].Tonnes == "12.500"
                && saved.TonnageEntries[0].Classification.BatteryCategory == "Portable"
                && saved.TonnageEntries[0].Classification.Chemistry == "Alkaline"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("  not-an-id  ", "674a1b2c3d4e5f6789012346", 2026, "Quarter 2")]
    [InlineData("674a1b2c3d4e5f6789012345", "not-an-id", 2026, "Quarter 2")]
    [InlineData("674a1b2c3d4e5f6789012345", "674a1b2c3d4e5f6789012346", 0, "Quarter 2")]
    [InlineData("674a1b2c3d4e5f6789012345", "674a1b2c3d4e5f6789012346", 2026, "   ")]
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
        await repository.DidNotReceive().FindCurrentAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await repository.DidNotReceive().CreateAsync(Arg.Any<PomSubmissionModel>(), Arg.Any<CancellationToken>());
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task CreateAsync_WhenChemistryDiffersOnlyByBlankText_ThrowsValidationException()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
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
                        Chemistry = "  Alkaline  "
                    },
                    Tonnes = "1.000"
                },
                new TonnageEntryBase
                {
                    Classification = new BatteryClassification
                    {
                        Chemistry = "Alkaline"
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
            .FindCurrentAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new PomSubmissionModel { Id = "674a1b2c3d4e5f6789012345", CreatedAtUtc = CreatedAt });
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
            .FindCurrentAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new PomSubmissionModel { Id = "674a1b2c3d4e5f6789012345", CreatedAtUtc = CreatedAt });
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

    private static PomSubmissionService Service(IPomSubmissionRepository repository, CapturingLogger<PomSubmissionService> logger) =>
        new(
            repository,
            new PomSubmissionMapper(Options.Create(new PomSubmissionConfig
            {
                InitialStatus = "Submitted",
                InitialVersion = 1,
                InitialActivity = "Placed"
            })),
            new PomSubmissionRequestValidator(),
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
                    Chemistry = "Alkaline"
                },
                Tonnes = "12.500"
            }
        ]
    };
}
