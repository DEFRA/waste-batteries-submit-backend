using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Queries;
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
    public void Filter_WhenOnlyTheIdIsSet_MatchesThatDocument()
    {
        // Arrange
        var query = new PomSubmissionQuery
        {
            Id = SamplePomSubmission.SchemeOrgId,
            IsCurrent = null
        };

        // Act
        var filter = PomSubmissionRepository.Filter(query);

        // Assert
        Assert.Equal(IdClause(SamplePomSubmission.SchemeOrgId), Render(filter));
    }

    [Fact]
    public void Filter_WhenOnlyTheSchemeIsSet_MatchesThatScheme()
    {
        // Arrange
        var query = new PomSubmissionQuery
        {
            SchemeOrgId = SamplePomSubmission.SchemeOrgId,
            IsCurrent = null
        };

        // Act
        var filter = PomSubmissionRepository.Filter(query);

        // Assert
        Assert.Equal(ObjectIdClause("schemeOrgId", SamplePomSubmission.SchemeOrgId), Render(filter));
    }

    [Fact]
    public void Filter_WhenOnlyTheMemberIsSet_MatchesThatMember()
    {
        // Arrange
        var query = new PomSubmissionQuery
        {
            MemberOrgId = SamplePomSubmission.MemberOrgId,
            IsCurrent = null
        };

        // Act
        var filter = PomSubmissionRepository.Filter(query);

        // Assert
        Assert.Equal(ObjectIdClause("memberOrgId", SamplePomSubmission.MemberOrgId), Render(filter));
    }

    [Fact]
    public void Filter_WhenYearsAreSet_MatchesAnyOfThoseYears()
    {
        // Arrange
        var query = new PomSubmissionQuery
        {
            CompliancePeriodYears = [2024, SamplePomSubmission.CompliancePeriodYear],
            IsCurrent = null
        };

        // Act
        var filter = PomSubmissionRepository.Filter(query);

        // Assert
        Assert.Equal(
            """{ "compliancePeriodYear" : { "$in" : [2024, 2026] } }""",
            Render(filter));
    }

    [Fact]
    public void Filter_WhenQuartersAreSet_MatchesAnyOfThoseQuarters()
    {
        // Arrange
        var query = new PomSubmissionQuery
        {
            CompliancePeriodQuarters = ["Quarter 1", SamplePomSubmission.CompliancePeriodQuarter],
            IsCurrent = null
        };

        // Act
        var filter = PomSubmissionRepository.Filter(query);

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
        var query = new PomSubmissionQuery { IsCurrent = isCurrent };

        // Act
        var filter = PomSubmissionRepository.Filter(query);

        // Assert
        Assert.Equal($$"""{ "isCurrent" : {{isCurrent.ToString().ToLowerInvariant()}} }""", Render(filter));
    }

    [Fact]
    public void Filter_WhenEveryCriterionIsSet_CombinesThemWithAnd()
    {
        // Arrange
        var query = new PomSubmissionQuery
        {
            Id = SamplePomSubmission.SchemeOrgId,
            SchemeOrgId = SamplePomSubmission.SchemeOrgId,
            MemberOrgId = OtherMemberOrgId,
            CompliancePeriodYears = [2024, SamplePomSubmission.CompliancePeriodYear],
            CompliancePeriodQuarters = ["Quarter 1", SamplePomSubmission.CompliancePeriodQuarter],
            IsCurrent = true
        };

        // Act
        var filter = PomSubmissionRepository.Filter(query);

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
        var query = new PomSubmissionQuery
        {
            SchemeOrgId = SamplePomSubmission.SchemeOrgId,
            CompliancePeriodYears = [],
            CompliancePeriodQuarters = [],
            IsCurrent = null
        };

        // Act
        var filter = PomSubmissionRepository.Filter(query);

        // Assert
        Assert.Equal(ObjectIdClause("schemeOrgId", SamplePomSubmission.SchemeOrgId), Render(filter));
    }

    [Fact]
    public void Filter_WhenNothingIsSet_Throws()
    {
        // Arrange
        var query = new PomSubmissionQuery { IsCurrent = null };

        // Act
        var act = () => PomSubmissionRepository.Filter(query);

        // Assert
        var exception = Assert.Throws<ArgumentException>(act);
        Assert.Equal("query", exception.ParamName);
    }

    private static string IdClause(string id) =>
        $$"""{ "_id" : { "$oid" : "{{id}}" } }""";

    private static string ObjectIdClause(string field, string id) =>
        $$"""{ "{{field}}" : { "$oid" : "{{id}}" } }""";

    private static string Render(FilterDefinition<PomSubmissionModel> filter)
    {
        var registry = BsonSerializer.SerializerRegistry;
        var args = new RenderArgs<PomSubmissionModel>(
            registry.GetSerializer<PomSubmissionModel>(),
            registry,
            default,
            false,
            false,
            false,
            null);
        return filter.Render(args).ToString();
    }
}
