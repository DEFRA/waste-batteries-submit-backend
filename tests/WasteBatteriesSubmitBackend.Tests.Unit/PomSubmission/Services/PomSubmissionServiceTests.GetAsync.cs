using FluentValidation;
using NSubstitute;
using WasteBatteriesSubmitBackend.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Queries;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Services;
using WasteBatteriesSubmitBackend.Tests.Unit.Utils.Logging;

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
            Id = "674a1b2c3d4e5f6789012345",
            CreatedAtUtc = CreatedAt
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
        var submission = await service.GetAsync("674a1b2c3d4e5f6789012345", TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(submission);
        await repository.Received(1).FindAsync(
            new PomSubmissionQuery { Id = "674a1b2c3d4e5f6789012345", IsCurrent = null },
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
        await service.GetAsync("  674a1b2c3d4e5f6789012345  ", TestContext.Current.CancellationToken);

        // Assert
        await repository.Received(1).FindAsync(
            new PomSubmissionQuery { Id = "674a1b2c3d4e5f6789012345", IsCurrent = null },
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
        var act = () => service.GetAsync(id, TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(act);
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
