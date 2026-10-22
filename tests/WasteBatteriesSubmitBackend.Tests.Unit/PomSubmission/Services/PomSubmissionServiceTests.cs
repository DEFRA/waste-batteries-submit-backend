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
using WasteBatteriesSubmitBackend.Utils.Time;

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
        var (level, message) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, level);
        Assert.Equal(
            $"Created POM submission {submission.Id} for scheme {submission.SchemeOrgId} member {submission.MemberOrgId}",
            message);
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
    public async Task CreateAsync_WhenMemberNameIsEmpty_ThrowsValidationException()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        var request = ValidRequest() with { MemberName = "" };

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
                    BatteryCategory = "Portable",
                    Chemistry = "Alkaline"
                },
                Tonnes = "12.500"
            }
        ]
    };

    private sealed class FixedTime(DateTime utcNow) : ITimeProvider
    {
        public DateTime UtcNow => utcNow;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
