using FluentValidation;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using WasteBatteriesSubmitBackend.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Filters;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Services;
using WasteBatteriesSubmitBackend.Tests.Common;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Services;

public partial class PomSubmissionServiceTests
{
    [Fact]
    public async Task AmendAsync_WhenCurrentSubmissionExists_SavesAReplacementInOneTransactionAndLogsIt()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var current = ReturnCurrentSubmission(repository);
        var session = Substitute.For<IClientSessionHandle>();
        repository
            .SupersedeCurrentAsync(session, current.Id, s_createdAt, Arg.Any<CancellationToken>())
            .Returns(true);
        var unitOfWork = UnitOfWork(session);
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger, unitOfWork);
        var request = ValidRequest();

        // Act
        var submission = await service.AmendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(current.Version + 1, submission.Version);
        await repository.Received(1).FindAsync(
            Arg.Is<PomSubmissionFilter>(filter =>
                filter.SchemeOrgId == request.SchemeOrgId
                && filter.MemberOrgId == request.MemberOrgId
                && filter.CompliancePeriodYears != null
                && filter.CompliancePeriodYears.SequenceEqual(new[] { request.CompliancePeriodYear })
                && filter.CompliancePeriodQuarters != null
                && filter.CompliancePeriodQuarters.SequenceEqual(new[] { request.CompliancePeriodQuarter })
                && filter.IsCurrent == true),
            new PageRequest { PageSize = MaxPageSize },
            Arg.Any<CancellationToken>());
        Received.InOrder(() =>
        {
            repository.SupersedeCurrentAsync(session, current.Id, s_createdAt, Arg.Any<CancellationToken>());
            repository.CreateAsync(
                session,
                Arg.Is<PomSubmissionModel>(saved =>
                    saved.Id == submission.Id
                    && saved.SupersedesSubmissionId == current.Id
                    && saved.IsCurrent
                    && saved.UpdatedAtUtc == null
                    && saved.Version == current.Version + 1
                    && saved.Status == "Submitted"
                    && saved.CreatedAtUtc == s_createdAt
                    && saved.MemberName == SamplePomSubmission.MemberName),
                Arg.Any<CancellationToken>());
        });
        await repository.DidNotReceive().CreateAsync(Arg.Any<PomSubmissionModel>(), Arg.Any<CancellationToken>());
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(
            $"Amended POM submission {submission.Id} superseding {current.Id} for scheme {submission.SchemeOrgId} member {submission.MemberOrgId}",
            entry.Message);
    }

    [Fact]
    public async Task AmendAsync_WhenSaveFails_DoesNotLog()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        ReturnCurrentSubmission(repository);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork
            .ExecuteAsync(
                Arg.Any<Func<IMongoTransaction, CancellationToken, Task<PomSubmissionModel>>>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database unavailable"));
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger, unitOfWork);

        // Act
        Task<PomSubmissionModel> Act() => service.AmendAsync(ValidRequest(), TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(Act);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task AmendAsync_WhenTheCurrentRowWasReplaced_ThrowsValidationExceptionAndDoesNotInsert()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var current = ReturnCurrentSubmission(repository);
        var session = Substitute.For<IClientSessionHandle>();
        repository
            .SupersedeCurrentAsync(session, current.Id, s_createdAt, Arg.Any<CancellationToken>())
            .Returns(false);
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger, UnitOfWork(session));

        // Act
        Task<PomSubmissionModel> Act() => service.AmendAsync(ValidRequest(), TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(Act);
        var error = Assert.Single(exception.Errors);
        Assert.Equal("Submission", error.PropertyName);
        Assert.Equal(
            "The current submission changed before this amendment was saved.",
            error.ErrorMessage);
        await repository.DidNotReceive().CreateAsync(
            Arg.Any<IClientSessionHandle>(),
            Arg.Any<PomSubmissionModel>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task AmendAsync_WhenTheReplacementInsertConflicts_ThrowsValidationException()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var current = ReturnCurrentSubmission(repository);
        var session = Substitute.For<IClientSessionHandle>();
        repository
            .SupersedeCurrentAsync(session, current.Id, s_createdAt, Arg.Any<CancellationToken>())
            .Returns(true);
        repository
            .CreateAsync(session, Arg.Any<PomSubmissionModel>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DuplicateKeyOnSaveException(new InvalidOperationException("duplicate key")));
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger, UnitOfWork(session));

        // Act
        Task<PomSubmissionModel> Act() => service.AmendAsync(ValidRequest(), TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(Act);
        var error = Assert.Single(exception.Errors);
        Assert.Equal("Submission", error.PropertyName);
        Assert.Equal(
            "The current submission changed before this amendment was saved.",
            error.ErrorMessage);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task AmendAsync_WhenTheInsertFailsForAnotherReason_DoesNotConvertItToValidation()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var current = ReturnCurrentSubmission(repository);
        var session = Substitute.For<IClientSessionHandle>();
        repository
            .SupersedeCurrentAsync(session, current.Id, s_createdAt, Arg.Any<CancellationToken>())
            .Returns(true);
        repository
            .CreateAsync(session, Arg.Any<PomSubmissionModel>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database unavailable"));
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger, UnitOfWork(session));

        // Act
        Task<PomSubmissionModel> Act() => service.AmendAsync(ValidRequest(), TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(Act);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task AmendAsync_WhenNoCurrentSubmissionExists_ThrowsValidationException()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        ReturnNoSubmissions(repository);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger, unitOfWork);

        // Act
        Task<PomSubmissionModel> Act() => service.AmendAsync(ValidRequest(), TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(Act);
        var error = Assert.Single(exception.Errors);
        Assert.Equal("Submission", error.PropertyName);
        Assert.Equal(
            "A current submission does not exist for this scheme, member, year and quarter.",
            error.ErrorMessage);
        await unitOfWork.DidNotReceive().ExecuteAsync(
            Arg.Any<Func<IMongoTransaction, CancellationToken, Task<PomSubmissionModel>>>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task AmendAsync_WhenModelIsInvalidAndNoCurrentSubmissionExists_ReturnsAllErrors()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        ReturnNoSubmissions(repository);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger, unitOfWork);
        var request = ValidRequest() with { MemberName = "" };

        // Act
        Task<PomSubmissionModel> Act() => service.AmendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(Act);
        Assert.Contains(exception.Errors, error => error.PropertyName == "MemberName");
        Assert.Contains(
            exception.Errors,
            error => error.PropertyName == "Submission"
                && error.ErrorMessage == "A current submission does not exist for this scheme, member, year and quarter.");
        await unitOfWork.DidNotReceive().ExecuteAsync(
            Arg.Any<Func<IMongoTransaction, CancellationToken, Task<PomSubmissionModel>>>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task AmendAsync_WhenModelIsInvalidAndCurrentSubmissionExists_ReturnsTheFieldError()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        ReturnCurrentSubmission(repository);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger, unitOfWork);
        var request = ValidRequest() with { MemberName = "" };

        // Act
        Task<PomSubmissionModel> Act() => service.AmendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(Act);
        var error = Assert.Single(exception.Errors);
        Assert.Equal("MemberName", error.PropertyName);
        await unitOfWork.DidNotReceive().ExecuteAsync(
            Arg.Any<Func<IMongoTransaction, CancellationToken, Task<PomSubmissionModel>>>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task AmendAsync_WhenTonnageEntriesAreEmpty_ThrowsValidationException()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        ReturnNoSubmissions(repository);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger, unitOfWork);
        var request = ValidRequest() with { TonnageEntries = [] };

        // Act
        Task<PomSubmissionModel> Act() => service.AmendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<ValidationException>(Act);
        await repository.Received(1).FindAsync(
            Arg.Any<PomSubmissionFilter>(),
            Arg.Any<PageRequest>(),
            Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().ExecuteAsync(
            Arg.Any<Func<IMongoTransaction, CancellationToken, Task<PomSubmissionModel>>>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task AmendAsync_WhenStringsHaveSurroundingWhitespace_SavesTheTrimmedSubmission()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var current = ReturnCurrentSubmission(repository);
        var session = Substitute.For<IClientSessionHandle>();
        repository
            .SupersedeCurrentAsync(session, current.Id, s_createdAt, Arg.Any<CancellationToken>())
            .Returns(true);
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger, UnitOfWork(session));
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
        await service.AmendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        await repository.Received(1).FindAsync(
            Arg.Is<PomSubmissionFilter>(filter =>
                filter.SchemeOrgId == SamplePomSubmission.SchemeOrgId
                && filter.MemberOrgId == SamplePomSubmission.MemberOrgId
                && filter.CompliancePeriodYears != null
                && filter.CompliancePeriodYears.SequenceEqual(s_trimmedCompliancePeriodYear)
                && filter.CompliancePeriodQuarters != null
                && filter.CompliancePeriodQuarters.SequenceEqual(s_trimmedCompliancePeriodQuarter)
                && filter.IsCurrent == true),
            new PageRequest { PageSize = MaxPageSize },
            Arg.Any<CancellationToken>());
        await repository.Received(1).CreateAsync(
            session,
            Arg.Is<PomSubmissionModel>(saved =>
                saved.SupersedesSubmissionId == current.Id
                && saved.MemberName == SamplePomSubmission.MemberName
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
    public async Task AmendAsync_WhenCurrentSubmissionKeyIsNotWellFormed_DoesNotLookItUp(
        string schemeOrgId,
        string memberOrgId,
        int year,
        string quarter)
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger, unitOfWork);
        var request = ValidRequest() with
        {
            SchemeOrgId = schemeOrgId,
            MemberOrgId = memberOrgId,
            CompliancePeriodYear = year,
            CompliancePeriodQuarter = quarter
        };

        // Act
        Task<PomSubmissionModel> Act() => service.AmendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<ValidationException>(Act);
        await repository.DidNotReceive().FindAsync(
            Arg.Any<PomSubmissionFilter>(),
            Arg.Any<PageRequest>(),
            Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().ExecuteAsync(
            Arg.Any<Func<IMongoTransaction, CancellationToken, Task<PomSubmissionModel>>>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task AmendAsync_WhenChemistryDiffersOnlyByBlankText_ThrowsValidationException()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        ReturnCurrentSubmission(repository);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger, unitOfWork);
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
        Task<PomSubmissionModel> Act() => service.AmendAsync(request, TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(Act);
        Assert.Contains(
            exception.Errors,
            error => error.PropertyName == "TonnageEntries"
                && error.ErrorMessage == "Each classification can only be reported once in a submission.");
        await unitOfWork.DidNotReceive().ExecuteAsync(
            Arg.Any<Func<IMongoTransaction, CancellationToken, Task<PomSubmissionModel>>>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(logger.Entries);
    }

    private static PomSubmissionModel ReturnCurrentSubmission(IPomSubmissionRepository repository)
    {
        var current = new PomSubmissionModel
        {
            Id = ObjectId.GenerateNewId().ToString(),
            CreatedAtUtc = s_createdAt,
            Version = 4,
            IsCurrent = true
        };
        repository
            .FindAsync(Arg.Any<PomSubmissionFilter>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaginatedList<PomSubmissionModel>([current], 1, 1, 1));
        return current;
    }

    private static IUnitOfWork UnitOfWork(IClientSessionHandle session)
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var transaction = Substitute.For<IMongoTransaction>();
        transaction.Session.Returns(session);
        unitOfWork
            .ExecuteAsync(
                Arg.Any<Func<IMongoTransaction, CancellationToken, Task<PomSubmissionModel>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var work = call.Arg<Func<IMongoTransaction, CancellationToken, Task<PomSubmissionModel>>>();
                return work(transaction, call.Arg<CancellationToken>());
            });
        return unitOfWork;
    }
}
