using FluentValidation;
using NSubstitute;
using WasteBatteriesSubmitBackend.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Queries;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Services;
using WasteBatteriesSubmitBackend.Tests.Common;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Services;

public partial class PomSubmissionServiceTests
{
    [Fact]
    public async Task GetAsync_WhenSubmissionExists_ReturnsIt()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        var stored = new PomSubmissionModel
        {
            Id = SamplePomSubmission.SchemeOrgId,
            CreatedAtUtc = s_createdAt
        };
        repository
            .FindAsync(
                new PomSubmissionQuery { Id = stored.Id, IsCurrent = null },
                new PageRequest(),
                Arg.Any<CancellationToken>())
            .Returns(new PaginatedList<PomSubmissionModel>([stored], 1, 1, 1));

        // Act
        var submission = await service.GetAsync(stored.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(stored, submission);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task GetAsync_WhenSubmissionDoesNotExist_ReturnsNull()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        repository
            .FindAsync(Arg.Any<PomSubmissionQuery>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaginatedList<PomSubmissionModel>([], 0, 1, 0));

        // Act
        var submission = await service.GetAsync(SamplePomSubmission.SchemeOrgId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(submission);
        await repository.Received(1).FindAsync(
            new PomSubmissionQuery { Id = SamplePomSubmission.SchemeOrgId, IsCurrent = null },
            new PageRequest(),
            Arg.Any<CancellationToken>());
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task GetAsync_WhenIdHasSurroundingWhitespace_LooksUpTheTrimmedId()
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);
        repository
            .FindAsync(Arg.Any<PomSubmissionQuery>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaginatedList<PomSubmissionModel>([], 0, 1, 0));

        // Act
        await service.GetAsync($"  {SamplePomSubmission.SchemeOrgId}  ", TestContext.Current.CancellationToken);

        // Assert
        await repository.Received(1).FindAsync(
            new PomSubmissionQuery { Id = SamplePomSubmission.SchemeOrgId, IsCurrent = null },
            new PageRequest(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("not-an-id")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetAsync_WhenIdIsNotAnObjectId_ThrowsValidationException(string id)
    {
        // Arrange
        var repository = Substitute.For<IPomSubmissionRepository>();
        var logger = new CapturingLogger<PomSubmissionService>();
        var service = Service(repository, logger);

        // Act
        Task<PomSubmissionModel?> Act() => service.GetAsync(id, TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(Act);
        var error = Assert.Single(exception.Errors);
        Assert.Equal("Id", error.PropertyName);
        Assert.Equal("id must be an ObjectId.", error.ErrorMessage);
        await repository.DidNotReceive().FindAsync(
            Arg.Any<PomSubmissionQuery>(),
            Arg.Any<PageRequest>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(logger.Entries);
    }
}
