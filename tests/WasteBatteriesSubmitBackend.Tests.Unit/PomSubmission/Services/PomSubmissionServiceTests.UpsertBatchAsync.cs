using FluentValidation;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using WasteBatteriesSubmitBackend.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Filters;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Responses;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Services;
using WasteBatteriesSubmitBackend.Tests.Common;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Services;

public partial class PomSubmissionServiceTests
{
    private const string OtherMemberOrgId = "674a1b2c3d4e5f6789012347";

    [Fact]
    public async Task UpsertBatchAsync_WhenNoCurrentRowsExist_CreatesEverySubmissionInOneTransactionAndLogsIt()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        ReturnNoCurrents(repository);
        var session = Substitute.For<IClientSessionHandle>();
        AllowCreateMany(repository, session);
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger, BatchUnitOfWork(session));
        var first = ValidRequest();
        var second = RequestFor(OtherMemberOrgId, "Quarter 1");

        // Act
        var results = await service.UpsertBatchAsync([first, second], TestContext.Current.CancellationToken);
        var ids = results.Select(result => result.Id).ToArray();

        // Assert
        Assert.Equal(2, results.Count);
        Assert.NotEqual(ids[0], ids[1]);
        Assert.Equal(PomSubmissionUpsertAction.Created, results[0].Action);
        Assert.Equal(0, results[0].RequestIndex);
        Assert.Equal(PomSubmissionKey.From(first), KeyOf(results[0]));
        Assert.Equal(PomSubmissionUpsertAction.Created, results[1].Action);
        Assert.Equal(1, results[1].RequestIndex);
        Assert.Equal(PomSubmissionKey.From(second), KeyOf(results[1]));
        await repository.Received(1).FindCurrentAsync(
            Arg.Is<IReadOnlyCollection<PomSubmissionKey>>(keys =>
                keys.SequenceEqual(new[] { PomSubmissionKey.From(first), PomSubmissionKey.From(second) })),
            Arg.Any<CancellationToken>());
        await repository.DidNotReceive().SupersedeCurrentsAsync(
            Arg.Any<IClientSessionHandle>(),
            Arg.Any<IReadOnlyCollection<string>>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
        await repository.Received(1).CreateManyAsync(
            session,
            Arg.Is<IReadOnlyList<PomSubmissionModel>>(saved =>
                saved.Select(submission => submission.Id).SequenceEqual(ids)
                && saved.All(submission =>
                    submission.Version == 1
                    && submission.SupersedesSubmissionId == null
                    && submission.IsCurrent
                    && submission.UpdatedAtUtc == null
                    && submission.CreatedAtUtc == s_createdAt
                    && submission.Status == "Submitted")
                && saved[0].MemberOrgId == SamplePomSubmission.MemberOrgId
                && saved[1].MemberOrgId == OtherMemberOrgId
                && saved[1].CompliancePeriodQuarter == "Quarter 1"),
            Arg.Any<CancellationToken>());
        await repository.DidNotReceive().FindAsync(
            Arg.Any<PomSubmissionFilter>(),
            Arg.Any<PageRequest>(),
            Arg.Any<CancellationToken>());
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(6, entry.EventId.Id);
        Assert.Equal("Upserted batch of 2 POM submissions (2 created, 0 amended)", entry.Message);
    }

    [Fact]
    public async Task UpsertBatchAsync_WhenCurrentRowsExist_AmendsThemInOneTransactionAndLogsIt()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var firstCurrent = CurrentFor(ValidRequest(), version: 4);
        var secondRequest = RequestFor(OtherMemberOrgId);
        var secondCurrent = CurrentFor(secondRequest, version: 2);
        repository
            .FindCurrentAsync(Arg.Any<IReadOnlyCollection<PomSubmissionKey>>(), Arg.Any<CancellationToken>())
            .Returns(new List<PomSubmissionModel> { secondCurrent, firstCurrent });
        var session = Substitute.For<IClientSessionHandle>();
        SupersedeAll(repository, session);
        AllowCreateMany(repository, session);
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger, BatchUnitOfWork(session));

        // Act
        var results = await service.UpsertBatchAsync(
            [ValidRequest(), secondRequest],
            TestContext.Current.CancellationToken);
        var ids = results.Select(result => result.Id).ToArray();

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal(PomSubmissionUpsertAction.Amended, results[0].Action);
        Assert.Equal(0, results[0].RequestIndex);
        Assert.Equal(PomSubmissionKey.From(ValidRequest()), KeyOf(results[0]));
        Assert.Equal(PomSubmissionUpsertAction.Amended, results[1].Action);
        Assert.Equal(1, results[1].RequestIndex);
        Assert.Equal(PomSubmissionKey.From(secondRequest), KeyOf(results[1]));
        Received.InOrder(() =>
        {
            repository.SupersedeCurrentsAsync(
                session,
                Arg.Is<IReadOnlyCollection<string>>(currentIds =>
                    currentIds.SequenceEqual(new[] { firstCurrent.Id, secondCurrent.Id })),
                s_createdAt,
                Arg.Any<CancellationToken>());
            repository.CreateManyAsync(
                session,
                Arg.Is<IReadOnlyList<PomSubmissionModel>>(saved =>
                    saved.Select(submission => submission.Id).SequenceEqual(ids)
                    && saved[0].SupersedesSubmissionId == firstCurrent.Id
                    && saved[0].Version == 5
                    && saved[1].SupersedesSubmissionId == secondCurrent.Id
                    && saved[1].Version == 3
                    && saved.All(submission => submission.IsCurrent && submission.UpdatedAtUtc == null)),
                Arg.Any<CancellationToken>());
        });
        var entry = Assert.Single(logger.Entries);
        Assert.Equal("Upserted batch of 2 POM submissions (0 created, 2 amended)", entry.Message);
    }

    [Fact]
    public async Task UpsertBatchAsync_WhenTheBatchMixesNewAndExisting_CreatesAndAmendsTogether()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var current = CurrentFor(ValidRequest());
        repository
            .FindCurrentAsync(Arg.Any<IReadOnlyCollection<PomSubmissionKey>>(), Arg.Any<CancellationToken>())
            .Returns(new List<PomSubmissionModel> { current });
        var session = Substitute.For<IClientSessionHandle>();
        SupersedeAll(repository, session);
        AllowCreateMany(repository, session);
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger, BatchUnitOfWork(session));
        var created = RequestFor(OtherMemberOrgId) with { MemberName = "Other Batteries Ltd" };

        // Act
        var results = await service.UpsertBatchAsync(
            [ValidRequest(), created],
            TestContext.Current.CancellationToken);
        var ids = results.Select(result => result.Id).ToArray();

        // Assert
        Assert.Equal(PomSubmissionUpsertAction.Amended, results[0].Action);
        Assert.Equal(0, results[0].RequestIndex);
        Assert.Equal(PomSubmissionKey.From(ValidRequest()), KeyOf(results[0]));
        Assert.Equal(PomSubmissionUpsertAction.Created, results[1].Action);
        Assert.Equal(1, results[1].RequestIndex);
        Assert.Equal(PomSubmissionKey.From(created), KeyOf(results[1]));
        Received.InOrder(() =>
        {
            repository.SupersedeCurrentsAsync(
                session,
                Arg.Is<IReadOnlyCollection<string>>(currentIds => currentIds.SequenceEqual(new[] { current.Id })),
                s_createdAt,
                Arg.Any<CancellationToken>());
            repository.CreateManyAsync(
                session,
                Arg.Is<IReadOnlyList<PomSubmissionModel>>(saved =>
                    saved.Select(submission => submission.Id).SequenceEqual(ids)
                    && saved[0].Version == current.Version + 1
                    && saved[0].SupersedesSubmissionId == current.Id
                    && saved[0].MemberName == SamplePomSubmission.MemberName
                    && saved[1].Version == 1
                    && saved[1].SupersedesSubmissionId == null
                    && saved[1].MemberOrgId == OtherMemberOrgId
                    && saved[1].MemberName == "Other Batteries Ltd"
                    && saved[0].CreatedAtUtc == saved[1].CreatedAtUtc),
                Arg.Any<CancellationToken>());
        });
        var entry = Assert.Single(logger.Entries);
        Assert.Equal("Upserted batch of 2 POM submissions (1 created, 1 amended)", entry.Message);
    }

    [Fact]
    public async Task UpsertBatchAsync_WhenThePostedIdUsesAnotherHexCase_AmendsTheCurrentRow()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var current = CurrentFor(ValidRequest());
        repository
            .FindCurrentAsync(Arg.Any<IReadOnlyCollection<PomSubmissionKey>>(), Arg.Any<CancellationToken>())
            .Returns(new List<PomSubmissionModel> { current });
        var session = Substitute.For<IClientSessionHandle>();
        SupersedeAll(repository, session);
        AllowCreateMany(repository, session);
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger, BatchUnitOfWork(session));
        var request = ValidRequest() with
        {
            SchemeOrgId = SamplePomSubmission.SchemeOrgId.ToUpperInvariant(),
            MemberOrgId = SamplePomSubmission.MemberOrgId.ToUpperInvariant()
        };

        // Act
        var results = await service.UpsertBatchAsync([request], TestContext.Current.CancellationToken);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal(PomSubmissionUpsertAction.Amended, result.Action);
        Assert.Equal(0, result.RequestIndex);
        Assert.Equal(PomSubmissionKey.From(ValidRequest()), KeyOf(result));
        await repository.Received(1).FindCurrentAsync(
            Arg.Is<IReadOnlyCollection<PomSubmissionKey>>(keys =>
                keys.Single() == PomSubmissionKey.From(ValidRequest())),
            Arg.Any<CancellationToken>());
        await repository.Received(1).CreateManyAsync(
            session,
            Arg.Is<IReadOnlyList<PomSubmissionModel>>(saved =>
                saved.Single().SupersedesSubmissionId == current.Id
                && saved.Single().Version == current.Version + 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpsertBatchAsync_WhenStringsHaveSurroundingWhitespace_SavesTheTrimmedSubmission()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        ReturnNoCurrents(repository);
        var session = Substitute.For<IClientSessionHandle>();
        AllowCreateMany(repository, session);
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger, BatchUnitOfWork(session));
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
                    Classification = new BatteryClassificationBase
                    {
                        Chemistry = $"  {SamplePomSubmission.Chemistry}  "
                    },
                    Tonnes = $"  {SamplePomSubmission.Tonnes}  "
                }
            ]
        };

        // Act
        var results = await service.UpsertBatchAsync([request], TestContext.Current.CancellationToken);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal(PomSubmissionUpsertAction.Created, result.Action);
        Assert.Equal(PomSubmissionKey.From(ValidRequest()), KeyOf(result));
        await repository.Received(1).FindCurrentAsync(
            Arg.Is<IReadOnlyCollection<PomSubmissionKey>>(keys =>
                keys.Single() == PomSubmissionKey.From(ValidRequest())),
            Arg.Any<CancellationToken>());
        await repository.Received(1).CreateManyAsync(
            session,
            Arg.Is<IReadOnlyList<PomSubmissionModel>>(saved =>
                saved.Single().MemberName == SamplePomSubmission.MemberName
                && saved.Single().MemberBprn == SamplePomSubmission.MemberBprn
                && saved.Single().SchemeMembershipRef == SamplePomSubmission.SchemeMembershipRef
                && saved.Single().DataBasis == SamplePomSubmission.DataBasis
                && saved.Single().CompliancePeriodQuarter == SamplePomSubmission.CompliancePeriodQuarter
                && saved.Single().TonnageEntries[0].Tonnes == SamplePomSubmission.Tonnes
                && saved.Single().TonnageEntries[0].Classification.Chemistry == SamplePomSubmission.Chemistry),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpsertBatchAsync_WhenTheBatchIsEmpty_ThrowsValidationException()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger, unitOfWork);

        // Act
        Task<IReadOnlyList<PomSubmissionUpsertResult>> Act() =>
            service.UpsertBatchAsync([], TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(Act);
        var error = Assert.Single(exception.Errors);
        Assert.Equal("Submissions", error.PropertyName);
        Assert.Equal("At least one submission is required.", error.ErrorMessage);
        await repository.DidNotReceive().FindCurrentAsync(
            Arg.Any<IReadOnlyCollection<PomSubmissionKey>>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task UpsertBatchAsync_WhenASubmissionIsEmpty_ThrowsValidationExceptionAndDoesNotSave()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger, unitOfWork);
        var requests = new List<PomSubmissionRequest> { ValidRequest(), null! };

        // Act
        Task<IReadOnlyList<PomSubmissionUpsertResult>> Act() =>
            service.UpsertBatchAsync(requests, TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(Act);
        var error = Assert.Single(exception.Errors);
        Assert.Equal("[1].Submission", error.PropertyName);
        Assert.Equal("A submission must not be empty.", error.ErrorMessage);
        await repository.DidNotReceive().FindCurrentAsync(
            Arg.Any<IReadOnlyCollection<PomSubmissionKey>>(),
            Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().ExecuteAsync(
            Arg.Any<Func<IMongoTransaction, CancellationToken, Task<List<PomSubmissionModel>>>>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task UpsertBatchAsync_WhenSeveralItemsAreInvalid_ReturnsEveryIndexedError()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        var repeated = ValidRequest() with { MemberName = "" };
        var notAYear = RequestFor(OtherMemberOrgId) with { CompliancePeriodYear = 0 };
        List<PomSubmissionRequest> requests = [ValidRequest(), repeated, notAYear];

        // Act
        Task<IReadOnlyList<PomSubmissionUpsertResult>> Act() =>
            service.UpsertBatchAsync(requests, TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(Act);
        Assert.Contains(exception.Errors, error => error.PropertyName == "[1].MemberName");
        Assert.Contains(
            exception.Errors,
            error => error.PropertyName == "[1].Submission"
                && error.ErrorMessage == "This submission repeats [0] for the same scheme, member, year and quarter.");
        Assert.Contains(exception.Errors, error => error.PropertyName == "[2].CompliancePeriodYear");
        Assert.DoesNotContain(exception.Errors, error => error.PropertyName == "[2].Submission");
        Assert.DoesNotContain(exception.Errors, error => error.PropertyName.StartsWith("[0].", StringComparison.Ordinal));
        await repository.DidNotReceive().FindCurrentAsync(
            Arg.Any<IReadOnlyCollection<PomSubmissionKey>>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task UpsertBatchAsync_WhenTheSameKeyIsRepeated_ThrowsValidationException()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        var repeated = ValidRequest() with
        {
            SchemeOrgId = $"  {SamplePomSubmission.SchemeOrgId.ToUpperInvariant()}  ",
            MemberOrgId = SamplePomSubmission.MemberOrgId.ToUpperInvariant()
        };

        // Act
        Task<IReadOnlyList<PomSubmissionUpsertResult>> Act() =>
            service.UpsertBatchAsync([ValidRequest(), repeated], TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(Act);
        var error = Assert.Single(exception.Errors);
        Assert.Equal("[1].Submission", error.PropertyName);
        Assert.Equal(
            "This submission repeats [0] for the same scheme, member, year and quarter.",
            error.ErrorMessage);
        await repository.DidNotReceive().FindCurrentAsync(
            Arg.Any<IReadOnlyCollection<PomSubmissionKey>>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task UpsertBatchAsync_WhenTheKeysAreNotWellFormed_DoesNotTreatThemAsRepeated()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        var request = ValidRequest() with { SchemeOrgId = "not-an-id" };

        // Act
        Task<IReadOnlyList<PomSubmissionUpsertResult>> Act() =>
            service.UpsertBatchAsync([request, request], TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(Act);
        Assert.Contains(exception.Errors, error => error.PropertyName == "[0].SchemeOrgId");
        Assert.Contains(exception.Errors, error => error.PropertyName == "[1].SchemeOrgId");
        Assert.DoesNotContain(
            exception.Errors,
            error => error.ErrorMessage.Contains("repeats", StringComparison.Ordinal));
        await repository.DidNotReceive().FindCurrentAsync(
            Arg.Any<IReadOnlyCollection<PomSubmissionKey>>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("  not-an-id  ", SamplePomSubmission.MemberOrgId, SamplePomSubmission.CompliancePeriodYear, SamplePomSubmission.CompliancePeriodQuarter)]
    [InlineData(SamplePomSubmission.SchemeOrgId, "not-an-id", SamplePomSubmission.CompliancePeriodYear, SamplePomSubmission.CompliancePeriodQuarter)]
    [InlineData(SamplePomSubmission.SchemeOrgId, SamplePomSubmission.MemberOrgId, 0, SamplePomSubmission.CompliancePeriodQuarter)]
    [InlineData(SamplePomSubmission.SchemeOrgId, SamplePomSubmission.MemberOrgId, SamplePomSubmission.CompliancePeriodYear, "   ")]
    public async Task UpsertBatchAsync_WhenTheKeyIsNotWellFormed_DoesNotLookItUp(
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
        Task<IReadOnlyList<PomSubmissionUpsertResult>> Act() =>
            service.UpsertBatchAsync([request], TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(Act);
        Assert.Contains(exception.Errors, error => error.PropertyName.StartsWith("[0].", StringComparison.Ordinal));
        await repository.DidNotReceive().FindCurrentAsync(
            Arg.Any<IReadOnlyCollection<PomSubmissionKey>>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task UpsertBatchAsync_WhenACurrentRowWasReplaced_ThrowsValidationExceptionAndDoesNotInsert()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        repository
            .FindCurrentAsync(Arg.Any<IReadOnlyCollection<PomSubmissionKey>>(), Arg.Any<CancellationToken>())
            .Returns(new List<PomSubmissionModel> { CurrentFor(ValidRequest()) });
        var session = Substitute.For<IClientSessionHandle>();
        repository
            .SupersedeCurrentsAsync(
                session,
                Arg.Any<IReadOnlyCollection<string>>(),
                s_createdAt,
                Arg.Any<CancellationToken>())
            .Returns(0L);
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger, BatchUnitOfWork(session));

        // Act
        Task<IReadOnlyList<PomSubmissionUpsertResult>> Act() =>
            service.UpsertBatchAsync([ValidRequest()], TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(Act);
        var error = Assert.Single(exception.Errors);
        Assert.Equal("Submission", error.PropertyName);
        Assert.Equal("A current submission changed before this batch was saved.", error.ErrorMessage);
        await repository.DidNotReceive().CreateManyAsync(
            Arg.Any<IClientSessionHandle>(),
            Arg.Any<IReadOnlyList<PomSubmissionModel>>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(logger.Entries);
    }

    [Theory]
    [InlineData(1, "[1].Submission")]
    [InlineData(null, "Submission")]
    public async Task UpsertBatchAsync_WhenTheInsertConflicts_ThrowsValidationException(
        int? documentIndex,
        string propertyName)
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        ReturnNoCurrents(repository);
        var session = Substitute.For<IClientSessionHandle>();
        repository
            .CreateManyAsync(session, Arg.Any<IReadOnlyList<PomSubmissionModel>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DuplicateKeyOnSaveException(new InvalidOperationException("duplicate key"), documentIndex));
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger, BatchUnitOfWork(session));

        // Act
        Task<IReadOnlyList<PomSubmissionUpsertResult>> Act() =>
            service.UpsertBatchAsync(
                [ValidRequest(), RequestFor(OtherMemberOrgId)],
                TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(Act);
        var error = Assert.Single(exception.Errors);
        Assert.Equal(propertyName, error.PropertyName);
        Assert.Equal(
            "A current submission already exists for this scheme, member, year and quarter.",
            error.ErrorMessage);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task UpsertBatchAsync_WhenTheInsertFailsForAnotherReason_DoesNotConvertItToValidation()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        ReturnNoCurrents(repository);
        var session = Substitute.For<IClientSessionHandle>();
        repository
            .CreateManyAsync(session, Arg.Any<IReadOnlyList<PomSubmissionModel>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database unavailable"));
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger, BatchUnitOfWork(session));

        // Act
        Task<IReadOnlyList<PomSubmissionUpsertResult>> Act() =>
            service.UpsertBatchAsync([ValidRequest()], TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(Act);
        Assert.Empty(logger.Entries);
    }

    private static void ReturnNoCurrents(IPomSubmissionRepository repository) =>
        repository
            .FindCurrentAsync(Arg.Any<IReadOnlyCollection<PomSubmissionKey>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<PomSubmissionModel>());

    private static void AllowCreateMany(IPomSubmissionRepository repository, IClientSessionHandle session) =>
        repository
            .CreateManyAsync(session, Arg.Any<IReadOnlyList<PomSubmissionModel>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

    private static void SupersedeAll(IPomSubmissionRepository repository, IClientSessionHandle session) =>
        repository
            .SupersedeCurrentsAsync(
                session,
                Arg.Any<IReadOnlyCollection<string>>(),
                s_createdAt,
                Arg.Any<CancellationToken>())
            .Returns(call => (long)call.Arg<IReadOnlyCollection<string>>().Count);

    private static PomSubmissionKey KeyOf(PomSubmissionUpsertResult result) =>
        new(
            result.SchemeOrgId,
            result.MemberOrgId,
            result.CompliancePeriodYear,
            result.CompliancePeriodQuarter);

    private static PomSubmissionRequest RequestFor(string memberOrgId, string? quarter = null) =>
        ValidRequest() with
        {
            MemberOrgId = memberOrgId,
            CompliancePeriodQuarter = quarter ?? SamplePomSubmission.CompliancePeriodQuarter
        };

    private static PomSubmissionModel CurrentFor(PomSubmissionRequest request, int version = 4)
    {
        var key = PomSubmissionKey.From(request);
        return new PomSubmissionModel
        {
            Id = ObjectId.GenerateNewId().ToString(),
            SchemeOrgId = key.SchemeOrgId,
            MemberOrgId = key.MemberOrgId,
            CompliancePeriodYear = key.CompliancePeriodYear,
            CompliancePeriodQuarter = key.CompliancePeriodQuarter,
            CreatedAtUtc = s_createdAt,
            Version = version,
            IsCurrent = true
        };
    }

    private static IUnitOfWork BatchUnitOfWork(IClientSessionHandle session)
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var transaction = Substitute.For<IMongoTransaction>();
        transaction.Session.Returns(session);
        unitOfWork
            .ExecuteAsync(
                Arg.Any<Func<IMongoTransaction, CancellationToken, Task<List<PomSubmissionModel>>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var work = call.Arg<Func<IMongoTransaction, CancellationToken, Task<List<PomSubmissionModel>>>>();
                return work(transaction, call.Arg<CancellationToken>());
            });
        return unitOfWork;
    }
}
