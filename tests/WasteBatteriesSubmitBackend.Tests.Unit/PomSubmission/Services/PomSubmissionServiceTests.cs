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
    public async Task CreateAsync_saves_the_mapped_submission_and_logs_information()
    {
        var repository = Substitute.For<IPomSubmissionRepository>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        var request = ValidRequest();

        var submission = await service.CreateAsync(request, TestContext.Current.CancellationToken);

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
    public async Task CreateAsync_does_not_log_when_the_save_fails()
    {
        var repository = Substitute.For<IPomSubmissionRepository>();
        repository
            .CreateAsync(Arg.Any<PomSubmissionModel>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database unavailable"));
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(ValidRequest(), TestContext.Current.CancellationToken));

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_submission_with_no_tonnage_entries()
    {
        var repository = Substitute.For<IPomSubmissionRepository>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(ValidRequest() with { TonnageEntries = [] }, TestContext.Current.CancellationToken));

        await repository.DidNotReceive().CreateAsync(Arg.Any<PomSubmissionModel>(), Arg.Any<CancellationToken>());
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_submission_without_a_member_name()
    {
        var repository = Substitute.For<IPomSubmissionRepository>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(ValidRequest() with { MemberName = "" }, TestContext.Current.CancellationToken));

        await repository.DidNotReceive().CreateAsync(Arg.Any<PomSubmissionModel>(), Arg.Any<CancellationToken>());
        Assert.Empty(logger.Entries);
    }

    private static PomSubmissionService Service(IPomSubmissionRepository repository, CapturingLogger<PomSubmissionService> logger) =>
        new(
            repository,
            new PomSubmissionMapper(new FixedTime(CreatedAt), Options.Create(new PomSubmissionConfig
            {
                InitialStatus = "Submitted",
                InitialVersion = 1,
                InitialActivity = "Placed"
            })),
            new PomSubmissionRequestValidator(repository),
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
