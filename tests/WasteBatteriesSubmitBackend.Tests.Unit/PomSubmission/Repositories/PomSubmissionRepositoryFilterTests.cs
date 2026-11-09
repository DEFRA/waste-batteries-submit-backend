using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Filters;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.Tests.Common;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Repositories;

public class PomSubmissionRepositoryFilterTests
{
    private const string OtherMemberOrgId = "674a1b2c3d4e5f6789012347";

    public PomSubmissionRepositoryFilterTests()
    {
        MongoConventions.Register();
    }

    [Fact]
    public void ListOrder_WhenRendered_SortsSchemeThenMemberThenLatestPeriod()
    {
        // Act
        var order = PomSubmissionRepository.ListOrder();

        // Assert
        Assert.Equal(
            "{ \"schemeOrgId\" : 1, \"memberOrgId\" : 1, \"compliancePeriodYear\" : -1, "
            + "\"compliancePeriodQuarter\" : -1, \"version\" : -1, \"createdAtUtc\" : -1, \"_id\" : 1 }",
            Render(order));
    }

    [Fact]
    public void Filter_WhenOnlyTheIdIsSet_MatchesThatDocument()
    {
        // Arrange
        var submissionFilter = new PomSubmissionFilter
        {
            Id = SamplePomSubmission.SchemeOrgId,
            IsCurrent = null
        };

        // Act
        var filter = PomSubmissionRepository.Filter(submissionFilter);

        // Assert
        Assert.Equal(IdClause(SamplePomSubmission.SchemeOrgId), Render(filter));
    }

    [Fact]
    public void Filter_WhenOnlyTheSchemeIsSet_MatchesThatScheme()
    {
        // Arrange
        var submissionFilter = new PomSubmissionFilter
        {
            SchemeOrgId = SamplePomSubmission.SchemeOrgId,
            IsCurrent = null
        };

        // Act
        var filter = PomSubmissionRepository.Filter(submissionFilter);

        // Assert
        Assert.Equal(ObjectIdClause("schemeOrgId", SamplePomSubmission.SchemeOrgId), Render(filter));
    }

    [Fact]
    public void Filter_WhenOnlyTheMemberIsSet_MatchesThatMember()
    {
        // Arrange
        var submissionFilter = new PomSubmissionFilter
        {
            MemberOrgId = SamplePomSubmission.MemberOrgId,
            IsCurrent = null
        };

        // Act
        var filter = PomSubmissionRepository.Filter(submissionFilter);

        // Assert
        Assert.Equal(ObjectIdClause("memberOrgId", SamplePomSubmission.MemberOrgId), Render(filter));
    }

    [Fact]
    public void Filter_WhenYearsAreSet_MatchesAnyOfThoseYears()
    {
        // Arrange
        var submissionFilter = new PomSubmissionFilter
        {
            CompliancePeriodYears = [2024, SamplePomSubmission.CompliancePeriodYear],
            IsCurrent = null
        };

        // Act
        var filter = PomSubmissionRepository.Filter(submissionFilter);

        // Assert
        Assert.Equal(
            """{ "compliancePeriodYear" : { "$in" : [2024, 2026] } }""",
            Render(filter));
    }

    [Fact]
    public void Filter_WhenQuartersAreSet_MatchesAnyOfThoseQuarters()
    {
        // Arrange
        var submissionFilter = new PomSubmissionFilter
        {
            CompliancePeriodQuarters = ["Quarter 1", SamplePomSubmission.CompliancePeriodQuarter],
            IsCurrent = null
        };

        // Act
        var filter = PomSubmissionRepository.Filter(submissionFilter);

        // Assert
        Assert.Equal(
            """{ "compliancePeriodQuarter" : { "$in" : ["Quarter 1", "Quarter 2"] } }""",
            Render(filter));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Filter_WhenOnlyIsCurrentIsSet_MatchesThatFlag(bool isCurrent)
    {
        // Arrange
        var submissionFilter = new PomSubmissionFilter { IsCurrent = isCurrent };

        // Act
        var filter = PomSubmissionRepository.Filter(submissionFilter);

        // Assert
        Assert.Equal($$"""{ "isCurrent" : {{isCurrent.ToString().ToLowerInvariant()}} }""", Render(filter));
    }

    [Fact]
    public void Filter_WhenEveryCriterionIsSet_CombinesThemWithAnd()
    {
        // Arrange
        var submissionFilter = new PomSubmissionFilter
        {
            Id = SamplePomSubmission.SchemeOrgId,
            SchemeOrgId = SamplePomSubmission.SchemeOrgId,
            MemberOrgId = OtherMemberOrgId,
            CompliancePeriodYears = [2024, SamplePomSubmission.CompliancePeriodYear],
            CompliancePeriodQuarters = ["Quarter 1", SamplePomSubmission.CompliancePeriodQuarter],
            IsCurrent = true
        };

        // Act
        var filter = PomSubmissionRepository.Filter(submissionFilter);

        // Assert
        Assert.Equal(
            "{ "
            + $"\"_id\" : {{ \"$oid\" : \"{SamplePomSubmission.SchemeOrgId}\" }}, "
            + $"\"schemeOrgId\" : {{ \"$oid\" : \"{SamplePomSubmission.SchemeOrgId}\" }}, "
            + $"\"memberOrgId\" : {{ \"$oid\" : \"{OtherMemberOrgId}\" }}, "
            + "\"compliancePeriodYear\" : { \"$in\" : [2024, 2026] }, "
            + "\"compliancePeriodQuarter\" : { \"$in\" : [\"Quarter 1\", \"Quarter 2\"] }, "
            + "\"isCurrent\" : true }",
            Render(filter));
    }

    [Fact]
    public void Filter_WhenACriterionIsUnsetOrEmpty_LeavesItOut()
    {
        // Arrange
        var submissionFilter = new PomSubmissionFilter
        {
            SchemeOrgId = SamplePomSubmission.SchemeOrgId,
            CompliancePeriodYears = [],
            CompliancePeriodQuarters = [],
            IsCurrent = null
        };

        // Act
        var filter = PomSubmissionRepository.Filter(submissionFilter);

        // Assert
        Assert.Equal(ObjectIdClause("schemeOrgId", SamplePomSubmission.SchemeOrgId), Render(filter));
    }

    [Fact]
    public void Filter_WhenNothingIsSet_Throws()
    {
        // Arrange
        var submissionFilter = new PomSubmissionFilter { IsCurrent = null };

        // Act
        FilterDefinition<PomSubmissionModel> Act() => PomSubmissionRepository.Filter(submissionFilter);

        // Assert
        var exception = Assert.Throws<ArgumentException>(Act);
        Assert.Equal("filter", exception.ParamName);
    }

    [Fact]
    public void FilterCurrent_WhenOneKeyIsSet_MatchesThatCurrentRow()
    {
        // Arrange
        var keys = new[] { SampleKey() };

        // Act
        var filter = PomSubmissionRepository.FilterCurrent(keys);

        // Assert
        Assert.Equal(CurrentClause(SampleKey()), Render(filter));
    }

    [Fact]
    public void FilterCurrent_WhenSeveralKeysAreSet_MatchesAnyOfThoseCurrentRows()
    {
        // Arrange
        var first = SampleKey();
        var second = SampleKey() with { MemberOrgId = OtherMemberOrgId, CompliancePeriodQuarter = "Quarter 1" };

        // Act
        var filter = PomSubmissionRepository.FilterCurrent([first, second]);

        // Assert
        Assert.Equal(
            "{ \"isCurrent\" : true, \"$or\" : ["
            + CurrentMatch(first)
            + ", "
            + CurrentMatch(second)
            + "] }",
            Render(filter));
    }

    [Fact]
    public void FilterCurrent_WhenNoKeyIsSupplied_Throws()
    {
        // Act
        FilterDefinition<PomSubmissionModel> Act() => PomSubmissionRepository.FilterCurrent([]);

        // Assert
        var exception = Assert.Throws<ArgumentException>(Act);
        Assert.Equal("keys", exception.ParamName);
    }

    [Fact]
    public void FilterCurrent_WhenKeysAreNull_Throws()
    {
        // Act
        FilterDefinition<PomSubmissionModel> Act() => PomSubmissionRepository.FilterCurrent(null!);

        // Assert
        var exception = Assert.Throws<ArgumentNullException>(Act);
        Assert.Equal("keys", exception.ParamName);
    }

    private static PomSubmissionKey SampleKey() =>
        new(
            SamplePomSubmission.SchemeOrgId,
            SamplePomSubmission.MemberOrgId,
            SamplePomSubmission.CompliancePeriodYear,
            SamplePomSubmission.CompliancePeriodQuarter);

    private static string CurrentClause(PomSubmissionKey key) =>
        "{ \"isCurrent\" : true, \"$or\" : [" + CurrentMatch(key) + "] }";

    private static string CurrentMatch(PomSubmissionKey key) =>
        "{ "
        + "\"schemeOrgId\" : { \"$oid\" : \""
        + key.SchemeOrgId
        + "\" }, \"memberOrgId\" : { \"$oid\" : \""
        + key.MemberOrgId
        + "\" }, \"compliancePeriodYear\" : "
        + key.CompliancePeriodYear
        + ", \"compliancePeriodQuarter\" : \""
        + key.CompliancePeriodQuarter
        + "\" }";

    private static string IdClause(string id) =>
        $$"""{ "_id" : { "$oid" : "{{id}}" } }""";

    private static string ObjectIdClause(string field, string id) =>
        $$"""{ "{{field}}" : { "$oid" : "{{id}}" } }""";

    private static string Render(FilterDefinition<PomSubmissionModel> filter) =>
        filter.Render(RenderArgs()).ToString();

    private static string Render(SortDefinition<PomSubmissionModel> sort) =>
        sort.Render(RenderArgs()).ToString();

    private static RenderArgs<PomSubmissionModel> RenderArgs()
    {
        var registry = BsonSerializer.SerializerRegistry;
        return new RenderArgs<PomSubmissionModel>(
            registry.GetSerializer<PomSubmissionModel>(),
            registry,
            default,
            false,
            false,
            false,
            null);
    }
}
