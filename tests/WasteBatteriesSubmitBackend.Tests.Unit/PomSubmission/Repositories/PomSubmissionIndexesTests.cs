using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Repositories;

public class PomSubmissionIndexesTests
{
    [Fact]
    public void Create_defines_the_submission_indexes()
    {
        MongoConventions.Register();
        var indexes = PomSubmissionIndexes.Create(Builders<PomSubmissionModel>.IndexKeys);
        var args = RenderArgs();

        Assert.Equal(6, indexes.Count);

        var current = indexes[0];
        Assert.True(current.Options.Unique);
        Assert.Equal(
            "{ \"schemeOrgId\" : 1, \"memberOrgId\" : 1, \"compliancePeriodYear\" : 1, \"compliancePeriodQuarter\" : 1 }",
            current.Keys.Render(args).ToString());
        Assert.Equal("{ \"isCurrent\" : true }", PartialFilter(current).Render(args).ToString());

        Assert.Equal(
            "{ \"schemeOrgId\" : 1, \"compliancePeriodYear\" : 1, \"compliancePeriodQuarter\" : 1 }",
            indexes[1].Keys.Render(args).ToString());
        Assert.NotEqual(true, indexes[1].Options?.Unique);

        Assert.Equal(
            "{ \"memberOrgId\" : 1, \"compliancePeriodYear\" : 1 }",
            indexes[2].Keys.Render(args).ToString());
        Assert.Equal("{ \"isCurrent\" : true }", PartialFilter(indexes[2]).Render(args).ToString());

        Assert.Equal("{ \"memberBprn\" : 1 }", indexes[3].Keys.Render(args).ToString());

        Assert.Equal(
            "{ \"tonnageEntries.classification.chemistry\" : 1, \"tonnageEntries.classification.batteryCategory\" : 1 }",
            indexes[4].Keys.Render(args).ToString());

        Assert.Equal("{ \"supersedesSubmissionId\" : 1 }", indexes[5].Keys.Render(args).ToString());
        Assert.True(indexes[5].Options.Sparse);
    }

    private static FilterDefinition<PomSubmissionModel> PartialFilter(CreateIndexModel<PomSubmissionModel> index)
    {
        var options = Assert.IsType<CreateIndexOptions<PomSubmissionModel>>(index.Options);
        Assert.NotNull(options.PartialFilterExpression);
        return options.PartialFilterExpression;
    }

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
