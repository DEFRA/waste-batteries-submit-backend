using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using WasteBatteriesSubmitBackend.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Queries;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;

namespace WasteBatteriesSubmitBackend.Tests.Integration.PomSubmission;

public partial class PomSubmissionEndpointsTests
{
    [Fact]
    public async Task FindAsync_WhenQueryHasNoCriteria_Throws()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = fixture.Services.GetRequiredService<IPomSubmissionRepository>();

        // Act
        async Task Act() =>
            await repository.FindAsync(
                new PomSubmissionQuery { IsCurrent = null },
                new PageRequest(),
                cancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(Act);
        Assert.Equal("query", exception.ParamName);
    }

    [Fact]
    public async Task List_WhenApplicationHasStarted_DefinesExpectedIndexes()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var indexes = await fixture.Submissions.Indexes.List(cancellationToken).ToListAsync(cancellationToken);

        // Assert
        Assert.DoesNotContain(indexes, index => index.Contains("expireAfterSeconds"));

        var current = Index(indexes, "schemeOrgId_1_memberOrgId_1_compliancePeriodYear_1_compliancePeriodQuarter_1");
        Assert.True(current["unique"].AsBoolean);
        Assert.True(current["partialFilterExpression"]["isCurrent"].AsBoolean);

        Index(indexes, "schemeOrgId_1_compliancePeriodYear_1_compliancePeriodQuarter_1");

        var memberHistory = Index(indexes, "memberOrgId_1_compliancePeriodYear_1");
        Assert.True(memberHistory["partialFilterExpression"]["isCurrent"].AsBoolean);
        Assert.False(memberHistory.Contains("unique"));

        Index(indexes, "memberBprn_1");
        Index(indexes, "tonnageEntries.classification.chemistry_1_tonnageEntries.classification.batteryCategory_1");

        var supersedes = Index(indexes, "supersedesSubmissionId_1");
        Assert.True(supersedes["sparse"].AsBoolean);
    }
}
